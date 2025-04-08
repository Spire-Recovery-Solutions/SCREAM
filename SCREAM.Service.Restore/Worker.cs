using CliWrap;
using CliWrap.Buffered;
using CliWrap.Builders;
using Cronos;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Restore;
using SCREAM.Data.Enums;
using System.Diagnostics;
using System.Net.Http.Json;

namespace SCREAM.Service.Restore
{
    public class Worker(ILogger<Worker> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration) : BackgroundService
    {
        private readonly HttpClient _httpClient = httpClientFactory.CreateClient("SCREAM");
        private string? _encryptionKey;
        private readonly int _maxRetries = 3;
        private readonly string _backupDirectory = "/backups";

        private string _mysqlHost = "";
        private string _mysqlUser = "";
        private string _mysqlPassword = "";
        private int _restoreThreads;
        private SemaphoreSlim _restoreSemaphore;
        private string GetConfigValue(string envKey, string configKey, string defaultValue = "")
        {
            var value = Environment.GetEnvironmentVariable(envKey);
            return !string.IsNullOrEmpty(value) ? value : configuration[configKey] ?? defaultValue;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            LoadConfiguration();
            Directory.CreateDirectory(_backupDirectory);
            logger.LogInformation("Verified backup directory exists at: {Directory}", _backupDirectory);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    logger.LogInformation("Restore Worker cycle started at: {Time}", DateTimeOffset.Now);

                    await ProcessCompletedBackupJobsAsync(cancellationToken);
                    await GenerateRestoreJobsAsync(cancellationToken);
                    await ProcessRestoreFilesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Critical error in main execution loop");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }

        private void LoadConfiguration()
        {
            _encryptionKey = GetConfigValue("MYSQL_BACKUP_ENCRYPTION_KEY", "MySqlBackup:EncryptionKey");

            // Load connection parameters.
            _mysqlHost = GetConfigValue("MYSQL_BACKUP_HOSTNAME", "MySqlBackup:HostName");
            _mysqlUser = GetConfigValue("MYSQL_BACKUP_USERNAME", "MySqlBackup:UserName");
            _mysqlPassword = GetConfigValue("MYSQL_BACKUP_PASSWORD", "MySqlBackup:Password");
            _restoreThreads = int.Parse(GetConfigValue("MYSQL_BACKUP_THREADS", "MySqlBackup:Threads", Environment.ProcessorCount.ToString()));
            _restoreSemaphore = new SemaphoreSlim(_restoreThreads);

            logger.LogInformation("Configuration loaded: Host={Host}, User={User}", _mysqlHost, _mysqlUser);
        }

        #region Restore Execution

        private async Task<bool> ExecuteRestoreForItemAsync(RestoreItem restoreItem, Tuple<string, string, string> connectionString, CancellationToken ct)
        {
            string filePath = GetRestoreFilePath(restoreItem);

            if (!File.Exists(filePath))
            {
                logger.LogWarning("{Schema}.{Name}: {Type} restore file not found at {Path}; skipping restore.",
                    restoreItem.DatabaseItem.Schema,
                    restoreItem.DatabaseItem.Name,
                    restoreItem.DatabaseItem.Type,
                    filePath);
                return false;
            }

            // Update restore item status to Running
            await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.Running);

            var argsBuilder = new ArgumentsBuilder()
                .Add("--max_allowed_packet=1073741824")
                .Add($"--host={connectionString.Item1}")
                .Add($"--user={connectionString.Item2}")
                .Add($"--password={connectionString.Item3}")
                .Add("--init-command=\"SET SESSION innodb_strict_mode=OFF;\"", false);

            // Schema is always needed except for global objects
            if (ShouldUseSchema(restoreItem.DatabaseItem.Type))
            {
                argsBuilder.Add(restoreItem.DatabaseItem.Schema);
            }

            try
            {
                logger.LogInformation("Starting {Type} restore for {Schema}.{Name} using file: {FilePath}",
                    restoreItem.DatabaseItem.Type,
                    restoreItem.DatabaseItem.Schema,
                    restoreItem.DatabaseItem.Name,
                    filePath);

                var result = await Cli.Wrap("/usr/bin/mysql")
                    .WithArguments(argsBuilder.Build())
                    .WithStandardInputPipe(PipeSource.FromFile(filePath))
                    .ExecuteBufferedAsync(ct);

                if (result.ExitCode != 0)
                {
                    logger.LogError("{Type} restore failed for {Schema}.{Name}: {Error}",
                        restoreItem.DatabaseItem.Type,
                        restoreItem.DatabaseItem.Schema,
                        restoreItem.DatabaseItem.Name,
                        result.StandardError);

                    restoreItem.RetryCount++;
                    await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.Faulted, result.StandardError);
                    return false;
                }

                logger.LogInformation("Successfully restored {Type} for {Schema}.{Name}",
                    restoreItem.DatabaseItem.Type,
                    restoreItem.DatabaseItem.Schema,
                    restoreItem.DatabaseItem.Name);

                await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.RanToCompletion);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Type} restore exception for {Schema}.{Name}: {Error}",
                    restoreItem.DatabaseItem.Type,
                    restoreItem.DatabaseItem.Schema,
                    restoreItem.DatabaseItem.Name,
                    ex.Message);

                restoreItem.RetryCount++;
                await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.Faulted, ex.Message);
                return false;
            }
        }

        private bool ShouldUseSchema(DatabaseItemType type)
        {
            return type switch
            {
                DatabaseItemType.TableStructure => true,
                DatabaseItemType.TableData => true,
                DatabaseItemType.View => true,
                DatabaseItemType.Trigger => true,
                DatabaseItemType.Event => false,
                DatabaseItemType.FunctionProcedure => true,
                _ => true
            };
        }
        private async Task UpdateRestoreItemStatusAsync(RestoreItem restoreItem, TaskStatus status, string? errorMessage = null)
        {
            try
            {
                var updateRequest = new
                {
                    restoreItem.Id,
                    restoreItem.RestoreJobId,
                    restoreItem.DatabaseItemId,
                    Status = status,
                    restoreItem.RetryCount,
                    ErrorMessage = errorMessage,
                    restoreItem.StartedAt,
                    CompletedAt = status == TaskStatus.RanToCompletion ? DateTime.UtcNow : restoreItem.CompletedAt
                };

                var response = await _httpClient.PostAsJsonAsync($"jobs/restore/items/{restoreItem.RestoreJobId}", updateRequest);

                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("Failed to update restore item {ItemId} status to {Status}: {StatusCode}. Response: {ResponseBody}",
                        restoreItem.Id, status, response.StatusCode, responseBody);
                    response.EnsureSuccessStatusCode();
                }
                else
                {
                    logger.LogInformation("Updated restore item {ItemId} status to {Status}", restoreItem.Id, status);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "HTTP request failed when updating restore item {ItemId} status", restoreItem.Id);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception updating restore item {ItemId} status", restoreItem.Id);
                throw;
            }
        }

        #endregion

        #region File Processing & Retry Logic

        private async Task ProcessRestoreFilesAsync(CancellationToken cancellationToken)
        {
            var totalStopwatch = Stopwatch.StartNew();
            logger.LogInformation("Starting ProcessRestoreFilesAsync at: {Time}", DateTimeOffset.Now);

            try
            {
                // 1) Scan directory
                var directoryStopwatch = Stopwatch.StartNew();
                var backupDirectoryInfo = new DirectoryInfo(_backupDirectory);
                int totalFiles = backupDirectoryInfo.GetFiles("*").Length;
                logger.LogInformation("Found {FileCount} files in directory: {Directory}", totalFiles, backupDirectoryInfo.FullName);
                directoryStopwatch.Stop();
                logger.LogDebug("Directory scan completed in {ElapsedMs}ms", directoryStopwatch.ElapsedMilliseconds);

                // 2) Decrypt & decompress
                var fileProcessingStopwatch = Stopwatch.StartNew();
                try
                {
                    logger.LogInformation("Starting file decryption process...");
                    await ProcessDecryptionAsync(cancellationToken, backupDirectoryInfo);

                    logger.LogInformation("Starting file decompression process...");
                    await ProcessDecompressionAsync(cancellationToken, backupDirectoryInfo);
                }
                finally
                {
                    fileProcessingStopwatch.Stop();
                    logger.LogInformation("File decryption and decompression completed in {ElapsedSec:F2} seconds", fileProcessingStopwatch.Elapsed.TotalSeconds);
                }

                // 3) Find SQL files
                var sqlFiles = backupDirectoryInfo.GetFiles("*.sql");
                logger.LogInformation("Found {FileCount} SQL files in directory", sqlFiles.Length);

                // 4) Fetch restore items
                var apiStopwatch = Stopwatch.StartNew();
                logger.LogInformation("Retrieving restore items from API...");
                var (jobId, restoreItems) = await GetRestoreItemsFromApiAsync(cancellationToken);
                apiStopwatch.Stop();

                if (jobId == 0 || !restoreItems.Any())
                {
                    logger.LogWarning("No active restore job or items to process. API check completed in {ElapsedMs}ms", apiStopwatch.ElapsedMilliseconds);
                    logger.LogInformation("ProcessRestoreFilesAsync completed with no items to process in {TotalSec:F2} seconds", totalStopwatch.Elapsed.TotalSeconds);
                    return;
                }

                logger.LogInformation("Retrieved {ItemCount} restore items for job {JobId} in {ElapsedMs}ms",
                    restoreItems.Count, jobId, apiStopwatch.ElapsedMilliseconds);

                // 5) Prepare databases
                var schemaStopwatch = Stopwatch.StartNew();
                logger.LogInformation("Preparing target databases...");
                await PrepareTargetDatabasesAsync(restoreItems, cancellationToken);
                schemaStopwatch.Stop();
                logger.LogInformation("Database preparation completed in {ElapsedSec:F2} seconds", schemaStopwatch.Elapsed.TotalSeconds);

                // 6) Process with dependencies (including parallel data phase)
                var connectionString = Tuple.Create(_mysqlHost, _mysqlUser, _mysqlPassword);
                var (allSuccessful, failedCount) = await ProcessItemsWithDependenciesAsync(restoreItems, connectionString, cancellationToken);

                // 7) Final logging & job update
                totalStopwatch.Stop();
                logger.LogInformation("Total restore process took {Minutes:F2} minutes ({TotalSec:F2} seconds)",
                    totalStopwatch.Elapsed.TotalMinutes, totalStopwatch.Elapsed.TotalSeconds);

                var statusMessage = allSuccessful
                    ? "All items restored successfully"
                    : $"Some items failed after maximum retry attempts: {failedCount} of {restoreItems.Count} items failed";
                var finalStatus = allSuccessful ? TaskStatus.RanToCompletion : TaskStatus.Faulted;

                logger.LogInformation("Updating job {JobId} status to {Status} with message: {Message}",
                    jobId, finalStatus, statusMessage);
                await UpdateJobStatusAsync(jobId, finalStatus, statusMessage);
            }
            catch (Exception ex)
            {
                totalStopwatch.Stop();
                logger.LogError(ex, "Critical error in ProcessRestoreFilesAsync after {TotalSec:F2} seconds", totalStopwatch.Elapsed.TotalSeconds);
                throw;
            }
        }


        private async Task PrepareDatabase(string schema, Tuple<string, string, string> connectionString,
            CancellationToken ct)
        {
            try
            {
                logger.LogInformation("Preparing database {Schema}", schema);

                await Cli.Wrap("/usr/bin/mysql")
                    .WithArguments(args => args
                        .Add($"--host={connectionString.Item1}")
                        .Add($"--user={connectionString.Item2}")
                        .Add($"--password={connectionString.Item3}")
                        .Add($"--execute=CREATE DATABASE IF NOT EXISTS `{schema}`")
                    )
                    .ExecuteBufferedAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Critical error preparing database {Schema}", schema);
                throw;
            }
        }

        #endregion

        private async Task<(bool allSuccessful, int failedCount)> ProcessItemsWithDependenciesAsync(List<RestoreItem> restoreItems, Tuple<string, string, string> connectionString, CancellationToken cancellationToken)
        {
            var processingOrder = new[]
            {
        DatabaseItemType.TableStructure,
        DatabaseItemType.FunctionProcedure,
        DatabaseItemType.Trigger,
        DatabaseItemType.Event,
        DatabaseItemType.View,
        DatabaseItemType.TableData
    };

            bool allOk = true;
            int failedCount = 0;

            var schemas = restoreItems.Select(i => i.DatabaseItem.Schema).Distinct();
            foreach (var schema in schemas)
            {
                await PrepareDatabase(schema, connectionString, cancellationToken);
            }

            foreach (var itemType in processingOrder)
            {
                var itemsOfType = restoreItems
                            .Where(i => i.DatabaseItem.Type == itemType)
                            .OrderBy(i => i.DatabaseItem.Schema)
                            .ThenBy(i => i.DatabaseItem.Name)
                            .ToList();

                if (!itemsOfType.Any()) continue;

                logger.LogInformation("Processing {Count} {Type} items across all schemas",
                    itemsOfType.Count, itemType);

                if (itemType == DatabaseItemType.TableData)
                {
                    await Parallel.ForEachAsync(itemsOfType, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = _restoreThreads,
                        CancellationToken = cancellationToken
                    }, async (item, token) =>
                    {
                        await _restoreSemaphore.WaitAsync(token);
                        try
                        {
                            bool ok = await ProcessItemWithRetriesAsync(item, connectionString, token);
                            if (!ok)
                            {
                                allOk = false;
                                Interlocked.Increment(ref failedCount);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Unhandled exception in parallel processing for item {ItemId}: {Error}",
                                item.Id, ex.Message);
                            allOk = false;
                            Interlocked.Increment(ref failedCount);
                        }
                        finally
                        {
                            _restoreSemaphore.Release();
                        }
                    });
                }
                else
                {
                    foreach (var item in itemsOfType)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            bool ok = await ProcessItemWithRetriesAsync(item, connectionString, cancellationToken);
                            if (!ok)
                            {
                                allOk = false;
                                failedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Unhandled exception processing item {ItemId}: {Error}",
                                item.Id, ex.Message);
                            allOk = false;
                            failedCount++;
                        }
                    }
                }
            }

            return (allOk, failedCount);
        }



        private async Task<bool> ProcessItemWithRetriesAsync(RestoreItem item, Tuple<string, string, string> connectionString, CancellationToken cancellationToken)
        {
            int attempt = 0;
            bool success = false;
            var itemSw = Stopwatch.StartNew();
            logger.LogInformation("Starting to process {Type} item {ItemId} ({Schema}.{Name})",
                item.DatabaseItem.Type, item.Id, item.DatabaseItem.Schema, item.DatabaseItem.Name);

            while (attempt <= _maxRetries && !success && !cancellationToken.IsCancellationRequested)
            {
                if (attempt > 0)
                {
                    int delaySeconds = (int)Math.Pow(2, attempt);
                    logger.LogInformation("Retry #{Attempt} for item {ItemId} ({Schema}.{Name}) after {Delay} seconds",
                        attempt, item.Id, item.DatabaseItem.Schema, item.DatabaseItem.Name, delaySeconds);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        logger.LogWarning("Task was canceled during retry delay for item {ItemId}", item.Id);
                        break;
                    }
                }

                var attemptSw = Stopwatch.StartNew();
                try
                {
                    string filePath = GetRestoreFilePath(item);
                    if (!File.Exists(filePath))
                    {
                        logger.LogWarning("Item {ItemId} ({Schema}.{Name}): Restore file not found at {Path}",
                            item.Id, item.DatabaseItem.Schema, item.DatabaseItem.Name, filePath);
                        item.RetryCount++;
                        await UpdateRestoreItemStatusAsync(item, TaskStatus.Faulted, $"Restore file not found: {Path.GetFileName(filePath)}");
                        break;
                    }

                    success = await ExecuteRestoreForItemAsync(item, connectionString, cancellationToken);
                    attemptSw.Stop();
                    if (success)
                    {
                        logger.LogInformation("Successfully processed item {ItemId} in {ElapsedMs}ms on attempt #{Attempt}",
                            item.Id, attemptSw.ElapsedMilliseconds, attempt + 1);
                        break;
                    }
                    else
                    {
                        logger.LogWarning("Failed to process item {ItemId} in {ElapsedMs}ms on attempt #{Attempt}",
                            item.Id, attemptSw.ElapsedMilliseconds, attempt + 1);
                    }
                }
                catch (Exception ex)
                {
                    attemptSw.Stop();
                    logger.LogError(ex, "Exception executing restore for item {ItemId} on attempt #{Attempt} after {ElapsedMs}ms",
                        item.Id, attempt + 1, attemptSw.ElapsedMilliseconds);
                    item.RetryCount++;
                    await UpdateRestoreItemStatusAsync(item, TaskStatus.Faulted, ex.Message);
                }

                attempt++;
            }

            itemSw.Stop();
            if (!success)
            {
                if (item.RetryCount >= _maxRetries)
                {
                    logger.LogWarning("Item {ItemId} ({Schema}.{Name}) failed after maximum retry attempts. Total time: {ElapsedSec:F2}s",
                        item.Id, item.DatabaseItem.Schema, item.DatabaseItem.Name, itemSw.Elapsed.TotalSeconds);
                }
                else if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Processing of item {ItemId} was canceled. Total time: {ElapsedSec:F2}s",
                        item.Id, itemSw.Elapsed.TotalSeconds);
                }
            }
            else
            {
                logger.LogInformation("Successfully completed item {ItemId} in {ElapsedSec:F2}s after {Attempts} attempt(s)",
                    item.Id, itemSw.Elapsed.TotalSeconds, attempt);
            }

            return success;
        }

        private string GetRestoreFilePath(RestoreItem restoreItem)
        {
            string fileSuffix = restoreItem.DatabaseItem.Type switch
            {
                DatabaseItemType.TableStructure => ".structure.sql",
                DatabaseItemType.TableData => ".data.sql",
                DatabaseItemType.View => ".view.sql",
                DatabaseItemType.Trigger => ".triggers.sql",
                DatabaseItemType.Event => ".events.sql",
                DatabaseItemType.FunctionProcedure => ".funcs.sql",
                _ => throw new ArgumentOutOfRangeException(nameof(restoreItem.DatabaseItem.Type))
            };

            return Path.Combine(_backupDirectory,
                $"{restoreItem.DatabaseItem.Schema}.{restoreItem.DatabaseItem.Name}{fileSuffix}");
        }


        private async Task PrepareTargetDatabasesAsync(List<RestoreItem> restoreItems, CancellationToken cancellationToken)
        {
            var schemas = restoreItems.Select(item => item.DatabaseItem.Schema).Distinct();

            // Ensure target databases exist
            foreach (var schema in schemas)
            {
                logger.LogInformation("Schema {Schema}: Creating database if not exists...", schema);
                try
                {
                    await Cli.Wrap("/usr/bin/mysql")
                        .WithArguments(args => args
                            .Add($"--host={_mysqlHost}")
                            .Add($"--user={_mysqlUser}")
                            .Add($"--password={_mysqlPassword}")
                            .Add($"--execute=\"CREATE DATABASE IF NOT EXISTS {schema};\"", false)
                        )
                        .ExecuteBufferedAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create database for schema {Schema}", schema);
                }
            }
        }

        private async Task ProcessDecompressionAsync(CancellationToken cancellationToken, DirectoryInfo backupDirectoryInfo)
        {
            var decompSw = Stopwatch.StartNew();

            var compressedFiles = backupDirectoryInfo.GetFiles().Where(file => file.Extension == ".xz").ToList();
            if (!compressedFiles.Any())
                return;

            logger.LogInformation("Found {Count} compressed files to decompress.", compressedFiles.Count);

            var tasks = new List<Task>();

            foreach (var compressedFile in compressedFiles)
            {
                await _restoreSemaphore.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessCompressedFileAsync(compressedFile, backupDirectoryInfo, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error decompressing file {FileName}", compressedFile.Name);
                    }
                    finally
                    {
                        _restoreSemaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
            logger.LogInformation("Completed decompression of files in {TotalMilliseconds}ms.", decompSw.ElapsedMilliseconds);
            await Task.Delay(10000, cancellationToken);
        }

        private async Task ProcessCompressedFileAsync(FileInfo compressedFile, DirectoryInfo backupDirectoryInfo, CancellationToken ct)
        {
            var localStopwatch = Stopwatch.StartNew();
            try
            {
                logger.LogInformation("Decompressing file: {FileName}", compressedFile.Name);

                var command = $"7z e {compressedFile.FullName} -o{backupDirectoryInfo.FullName} -aoa";
                var result = await Cli.Wrap("/bin/bash")
                    .WithArguments(new[] { "-c", command })
                    .ExecuteBufferedAsync(ct);

                if (result.ExitCode != 0)
                {
                    logger.LogError("Decompression failed for {FileName}: {Error}", compressedFile.Name, result.StandardError);
                }
                else
                {
                    // Delete the compressed file after successful decompression.
                    File.Delete(compressedFile.FullName);
                    logger.LogInformation("Successfully decompressed {FileName} in {Milliseconds}ms",
                        compressedFile.Name, localStopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception decompressing file {FileName}", compressedFile.Name);
            }
        }

        private async Task ProcessDecryptionAsync(CancellationToken cancellationToken, DirectoryInfo backupDirectoryInfo)
        {
            var encryptedFiles = backupDirectoryInfo.GetFiles().Where(file => file.Extension == ".enc").ToList();
            if (!encryptedFiles.Any())
                return;

            logger.LogInformation("Found {Count} encrypted files to decrypt.", encryptedFiles.Count);

            var tasks = new List<Task>();

            foreach (var encryptedFile in encryptedFiles)
            {
                await _restoreSemaphore.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessEncryptedFileAsync(encryptedFile, backupDirectoryInfo, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error decrypting file {FileName}", encryptedFile.Name);
                    }
                    finally
                    {
                        _restoreSemaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
            logger.LogInformation("Completed decryption of files.");
            await Task.Delay(10000, cancellationToken);
        }

        private async Task ProcessEncryptedFileAsync(FileInfo encryptedFile, DirectoryInfo backupDirectoryInfo, CancellationToken ct)
        {
            var localStopwatch = Stopwatch.StartNew();
            try
            {
                logger.LogInformation("Decrypting file: {FileName}", encryptedFile.Name);
                var outputFile = encryptedFile.FullName.Replace(".enc", "");
                var command = $"/usr/bin/openssl enc -d -aes-256-cbc -pbkdf2 -iter 20000 -in {encryptedFile.FullName} -out {outputFile} -k {_encryptionKey}";
                var result = await Cli.Wrap("/bin/bash")
                    .WithArguments(new[] { "-c", command })
                    .ExecuteBufferedAsync(ct);

                if (result.ExitCode != 0)
                {
                    logger.LogError("Decryption failed for {FileName}: {Error}", encryptedFile.Name, result.StandardError);
                }
                else
                {
                    File.Delete(encryptedFile.FullName);
                    logger.LogInformation("Successfully decrypted {FileName} in {Milliseconds}ms", encryptedFile.Name, localStopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception decrypting file {FileName}", encryptedFile.Name);
            }
        }


        #region API Integration

        private async Task<(long JobId, List<RestoreItem> Items)> GetRestoreItemsFromApiAsync(CancellationToken ct)
        {
            try
            {
                var activeJobs = await _httpClient.GetFromJsonAsync<List<RestoreJob>>($"jobs/restore?status={TaskStatus.Created}", ct);
                if (activeJobs == null || activeJobs.Count == 0)
                {
                    logger.LogWarning("No active restore jobs with status 'Created' found.");
                    return (0, new List<RestoreItem>());
                }

                var activeJob = activeJobs.First();
                if (activeJob.Status == TaskStatus.Created)
                {
                    await UpdateJobStatusAsync(activeJob.Id, TaskStatus.Running);
                }

                var items = await _httpClient.GetFromJsonAsync<List<RestoreItem>>(
                    $"jobs/restore/items/{activeJob.Id}?excludeTaskStatus={TaskStatus.RanToCompletion}", ct);

                logger.LogInformation("Retrieved {ItemCount} restore items for job {JobId}. Items: {ItemDetails}",
                    items?.Count ?? 0,
                    activeJob.Id,
                    string.Join("; ", items?.Select(i => $"Id:{i.Id}, Status:{i.Status}, Retry:{i.RetryCount}") ?? Array.Empty<string>())
                );

                return (activeJob.Id, items?.ToList() ?? new List<RestoreItem>());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve restore items: {Message}", ex.Message);
                return (0, new List<RestoreItem>());
            }
        }

        private async Task<bool> UpdateJobStatusAsync(long jobId, TaskStatus status, string? message = null)
        {
            try
            {
                var currentJob = await _httpClient.GetFromJsonAsync<RestoreJob>($"jobs/restore/{jobId}");
                if (currentJob == null)
                {
                    logger.LogWarning("Restore job {JobId} not found to update status", jobId);
                    return false;
                }

                var updatedJob = new RestoreJob
                {
                    Id = currentJob.Id,
                    Status = status,
                    IsCompressed = currentJob.IsCompressed,
                    IsEncrypted = currentJob.IsEncrypted,
                    RestorePlanId = currentJob.RestorePlanId,
                    StartedAt = currentJob.StartedAt != default ? currentJob.StartedAt : DateTime.UtcNow,
                    CompletedAt = (status == TaskStatus.RanToCompletion || status == TaskStatus.Faulted) ? DateTime.UtcNow : default,
                    CreatedAt = currentJob.CreatedAt
                };

                var putResponse = await _httpClient.PutAsJsonAsync($"jobs/restore/{currentJob.Id}", updatedJob);
                if (!putResponse.IsSuccessStatusCode)
                {
                    var responseBody = await putResponse.Content.ReadAsStringAsync();
                    logger.LogWarning("Failed to update restore job {JobId} status to {Status}: {StatusCode}. Response: {ResponseBody}",
                        currentJob.Id, status, putResponse.StatusCode, responseBody);
                    return false;
                }

                logger.LogInformation("Successfully updated restore job {JobId} status to {Status}", currentJob.Id, status);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update job status: {Message}", ex.Message);
                return false;
            }
        }

        private async Task GenerateRestoreJobsAsync(CancellationToken ct)
        {
            try
            {
                var eligiblePlans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>(
                    "plans/restore?isActive=true", ct) ?? new List<RestorePlan>();

                foreach (var plan in eligiblePlans)
                {
                    var existingJobs = await _httpClient.GetFromJsonAsync<List<RestoreJob>>(
                        $"jobs/restore?planId={plan.Id}", ct) ?? new List<RestoreJob>();

                    if (plan.ScheduleType == ScheduleType.OneTime && plan.LastRun.HasValue)
                    {
                        if (plan.LastRun.HasValue || existingJobs.Any())
                        {
                            logger.LogInformation("Skipping OneTime plan {PlanId} - already executed", plan.Id);
                            continue;
                        }
                    }
                    else if (existingJobs.Any(j => j.Status >= TaskStatus.Created && j.Status < TaskStatus.RanToCompletion))
                    {
                        logger.LogInformation("Skipping plan {PlanId} - active job exists", plan.Id);
                        continue;
                    }

                    bool shouldCreate = false;

                    switch (plan.ScheduleType)
                    {
                        case ScheduleType.OneTime:
                            shouldCreate = true;
                            break;

                        case ScheduleType.Repeating:
                            var nextRun = plan.GetNextRun(DateTime.UtcNow);
                            shouldCreate = nextRun <= DateTime.UtcNow;

                            if (!shouldCreate && plan.NextRun == null)
                            {
                                // Initialize next run time for new repeating plans
                                plan.NextRun = CronExpression.Parse(plan.ScheduleCron)
                                    .GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
                                await UpdateRestorePlan(plan, ct);
                            }
                            break;

                        case ScheduleType.Triggered:
                            if (plan.NextRun != null)
                            {
                                plan.NextRun = null;
                                await UpdateRestorePlan(plan, ct);
                            }
                            break;
                    }

                    if (!shouldCreate) continue;

                    if (await CreateRestoreJob(plan, ct))
                    {
                        // Update plan state after successful job creation
                        switch (plan.ScheduleType)
                        {
                            case ScheduleType.OneTime:
                                plan.LastRun = DateTime.UtcNow;
                                break;

                            case ScheduleType.Repeating:
                                plan.LastRun = DateTime.UtcNow;
                                plan.NextRun = CronExpression.Parse(plan.ScheduleCron)
                                    .GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
                                break;
                        }

                        await UpdateRestorePlan(plan, ct);
                        logger.LogInformation("Updated plan {PlanId} after job creation", plan.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating restore jobs");
            }
        }

        private async Task<bool> CreateRestoreJob(RestorePlan plan, CancellationToken ct)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"plans/restore/{plan.Id}/run", new { }, ct);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Successfully created restore job for plan {PlanId}", plan.Id);
                    return true;
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync(ct);
                    logger.LogError("Failed to create restore job for plan {PlanId}. Status: {StatusCode}, Content: {Content}",
                        plan.Id, response.StatusCode, responseContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception occurred while creating restore job for plan {PlanId}", plan.Id);
                return false;
            }
        }

        private async Task<bool> UpdateRestorePlan(RestorePlan plan, CancellationToken ct)
        {
            try
            {
                var updateResponse = await _httpClient.PostAsJsonAsync("plans/restore", plan, ct);

                if (updateResponse.IsSuccessStatusCode)
                {
                    logger.LogInformation("Updated restore plan {PlanId}", plan.Id);
                    return true;
                }
                else
                {
                    var error = await updateResponse.Content.ReadAsStringAsync(ct);
                    logger.LogError("Error updating restore plan {PlanId}: {Error}", plan.Id, error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception updating restore plan {PlanId}: {Error}", plan.Id, ex.Message);
                return false;
            }
        }

        private async Task ProcessCompletedBackupJobsAsync(CancellationToken ct)
        {
            try
            {
                var backupJobs = await _httpClient.GetFromJsonAsync<List<BackupJob>>("jobs/backup", ct);
                if (backupJobs == null)
                {
                    logger.LogWarning("No backup jobs received from API.");
                    return;
                }

                var completedJobs = backupJobs
                    .Where(job => job.Status == TaskStatus.RanToCompletion && !job.HasTriggeredRestore)
                    .ToList();

                foreach (var backupJob in completedJobs)
                {
                    logger.LogInformation("Processing backup job {BackupJobId} for restore triggering", backupJob.Id);

                    var restorePlans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>(
                        $"plans/restore?sourceBackupPlanId={backupJob.BackupPlanId}&scheduleType=Triggered", ct);

                    if (restorePlans == null || !restorePlans.Any())
                    {
                        logger.LogInformation("No triggered restore plans found for backup job {BackupJobId}", backupJob.Id);
                        continue;
                    }

                    foreach (var plan in restorePlans.Where(p => p.IsActive))
                    {
                        try
                        {
                            logger.LogInformation("Triggering restore for plan {PlanId} based on completed backup job {BackupJobId}",
                                plan.Id, backupJob.Id);

                            bool success = await CreateRestoreJob(plan, ct);

                            if (success)
                            {
                                if (!string.IsNullOrEmpty(plan.ScheduleCron))
                                {
                                    try
                                    {
                                        var cronExpression = CronExpression.Parse(plan.ScheduleCron);

                                        plan.LastRun = DateTime.UtcNow;

                                        plan.NextRun = cronExpression.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);

                                        await UpdateRestorePlan(plan, ct);

                                        logger.LogInformation(
                                            "Updated triggered plan {PlanId} with LastRun={LastRun}, NextRun={NextRun}",
                                            plan.Id, plan.LastRun, plan.NextRun);
                                    }
                                    catch (Exception cronEx)
                                    {
                                        logger.LogError(cronEx, "Error processing cron schedule for triggered plan {PlanId}", plan.Id);
                                    }
                                }
                                else
                                {
                                    plan.LastRun = DateTime.UtcNow;
                                    await UpdateRestorePlan(plan, ct);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error processing restore plan {PlanId} for backup job {BackupJobId}",
                                plan.Id, backupJob.Id);
                        }
                    }

                    backupJob.HasTriggeredRestore = true;
                    await _httpClient.PutAsJsonAsync($"jobs/backup/{backupJob.Id}", backupJob, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Critical error in ProcessCompletedBackupJobsAsync");
            }
        }

    }
}
#endregion