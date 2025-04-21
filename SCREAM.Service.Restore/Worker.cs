using CliWrap;
using CliWrap.Buffered;
using CliWrap.Builders;
using Cronos;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Restore;
using SCREAM.Data.Entities.StorageTargets;
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
            // Add validation for encryption key
            if (string.IsNullOrEmpty(_encryptionKey))
            {
                logger.LogError("Encryption key is not configured!");
                throw new InvalidOperationException("Encryption key is required");
            }
            else
            {
                logger.LogInformation("Encryption key configured successfully (present: {KeyPresent})",
                    !string.IsNullOrEmpty(_encryptionKey));
            }
            // Load connection parameters.
            _mysqlHost = GetConfigValue("MYSQL_BACKUP_HOSTNAME", "MySqlBackup:HostName");
            _mysqlUser = GetConfigValue("MYSQL_BACKUP_USERNAME", "MySqlBackup:UserName");
            _mysqlPassword = GetConfigValue("MYSQL_BACKUP_PASSWORD", "MySqlBackup:Password");
            _restoreThreads = int.Parse(GetConfigValue("MYSQL_BACKUP_THREADS", "MySqlBackup:Threads",
                Environment.ProcessorCount.ToString()));
            _restoreSemaphore = new SemaphoreSlim(_restoreThreads);

            logger.LogInformation("Configuration loaded: Host={Host}, User={User}", _mysqlHost, _mysqlUser);
        }

        #region Restore Execution

        private async Task<bool> ExecuteRestoreForItemAsync(RestoreItem restoreItem, Tuple<string, string, string> connectionString, string backupFolderPath,
    CancellationToken ct)
        {
            string filePath = GetRestoreFilePath(backupFolderPath, restoreItem);

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
                .Add("--max-allowed-packet=1073741824")
                .Add($"--host={connectionString.Item1}")
                .Add($"--user={connectionString.Item2}")
                .Add($"--password={connectionString.Item3}");

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
                if (status == TaskStatus.Running && restoreItem.StartedAt == default)
                {
                    restoreItem.StartedAt = DateTime.UtcNow;
                }

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
            logger.LogInformation("Starting ProcessRestoreFilesAsync");

            try
            {
                // 1) Get restore job with all related data
                var (restoreJob, restoreItems) = await GetRestoreJobWithDetailsAsync(cancellationToken);
                if (restoreJob == null || !restoreItems.Any())
                {
                    logger.LogInformation("No restore items to process.");
                    return;
                }

                // 2) Get all required entities in one flow
                var (restorePlan, backupPlan, storageTarget) = await GetRestoreDependenciesAsync(restoreJob, cancellationToken);
                var backupFolderPath = GetBackupFolderPath(storageTarget);

                if (!Directory.Exists(backupFolderPath))
                {
                    throw new DirectoryNotFoundException(
                        $"Backup directory not found: {backupFolderPath}. " +
                        "Verify storage target path is configured correctly.");
                }

                logger.LogInformation("backup Folder path exist at: " + backupFolderPath);
                // 3) Process files with all required data
                var backupDirectoryInfo = new DirectoryInfo(backupFolderPath);

                // 4) Decrypt & decompress
                logger.LogInformation("Starting file decryption process...");
                await ProcessDecryptionAsync(cancellationToken, backupDirectoryInfo);

                logger.LogInformation("Starting file decompression process...");
                await ProcessDecompressionAsync(cancellationToken, backupDirectoryInfo);

                // 5) Execute restore with all required context
                var connectionString = Tuple.Create(_mysqlHost, _mysqlUser, _mysqlPassword);
                var (allSuccessful, failedCount) = await ProcessItemsWithDependenciesAsync(
                    restoreItems,
                    connectionString,
                    backupFolderPath,
                    cancellationToken
                );

                // 6) Update job status with existing entity
                await UpdateJobStatusAsync(restoreJob,
                    allSuccessful ? TaskStatus.RanToCompletion : TaskStatus.Faulted,
                    allSuccessful ? "All items restored" : $"{failedCount} items failed",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Critical error in ProcessRestoreFilesAsync");
                throw;
            }
            finally
            {
                totalStopwatch.Stop();
                logger.LogInformation("ProcessRestoreFilesAsync completed in {Elapsed}s", totalStopwatch.Elapsed);
            }
        }

        private async Task<(RestoreJob? Job, List<RestoreItem> Items)> GetRestoreJobWithDetailsAsync(CancellationToken ct)
        {
            try
            {
                var activeJobs = await _httpClient.GetFromJsonAsync<List<RestoreJob>>(
                    "jobs/restore?statuses=Created&statuses=Running&statuses=WaitingToRun", ct);

                var activeJob = activeJobs?.FirstOrDefault();
                if (activeJob == null) return (null, new List<RestoreItem>());

                // Update job status if needed
                if (activeJob.Status == TaskStatus.Created || activeJob.Status == TaskStatus.WaitingToRun)
                {
                    await UpdateJobStatusAsync(activeJob, TaskStatus.Running, null, ct);
                }

                // Get items using existing job reference
                var items = await GetRestoreItemsForJobAsync(activeJob, ct);
                return (activeJob, items);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve restore job details");
                return (null, new List<RestoreItem>());
            }
        }

        private string GetBackupFolderPath(StorageTarget storageTarget)
        {
            if (storageTarget is not LocalStorageTarget localTarget)
                throw new NotSupportedException("Only local storage supported");

            var root = Environment.GetEnvironmentVariable("LOCAL_STORAGE_ROOT") ?? "/backups";
            root = root.TrimEnd('/', '\\');
            var path = localTarget.Path.Trim('/', '\\');
            var targetDirectory = $"{root}/{path}";

            return targetDirectory;
        }

        private async Task<(RestorePlan Plan, BackupPlan BackupPlan, StorageTarget StorageTarget)> GetRestoreDependenciesAsync(RestoreJob restoreJob, CancellationToken ct)
        {
            var restorePlan = await _httpClient.GetFromJsonAsync<RestorePlan>(
                $"plans/restore/{restoreJob.RestorePlanId}", ct)
                ?? throw new InvalidOperationException("Restore plan not found");

            var backupPlan = await _httpClient.GetFromJsonAsync<BackupPlan>(
                $"plans/backup/{restorePlan.SourceBackupPlanId}", ct)
                ?? throw new InvalidOperationException("Backup plan not found");

            var storageTarget = await _httpClient.GetFromJsonAsync<StorageTarget>(
                $"targets/storage/{backupPlan.StorageTargetId}", ct)
                ?? throw new InvalidOperationException("Storage target not found");

            return (restorePlan, backupPlan, storageTarget);
        }

        private async Task<List<RestoreItem>> GetRestoreItemsForJobAsync(RestoreJob job, CancellationToken ct)
        {
            var items = await _httpClient.GetFromJsonAsync<List<RestoreItem>>(
                $"jobs/restore/items/{job.Id}?statuses=Created&statuses=Running&statuses=Faulted&statuses=WaitingToRun", ct);

            return items?.ToList() ?? new List<RestoreItem>();
        }

        private async Task PrepareDatabase(string schema, Tuple<string, string, string> connectionString, CancellationToken ct)
        {
            try
            {
                logger.LogInformation("Preparing database {Schema}", schema);

                var result = await Cli.Wrap("/usr/bin/mysql")
                    .WithArguments(args => args
                        .Add($"--host={connectionString.Item1}")
                        .Add($"--user={connectionString.Item2}")
                        .Add($"--password={connectionString.Item3}")
                        .Add($"--execute=CREATE DATABASE IF NOT EXISTS `{schema}`")
                    )
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync(ct);

                if (result.ExitCode != 0)
                {
                    logger.LogError("Failed to create {Schema}. Exit code: {ExitCode}. Error: {Error}",
                        schema, result.ExitCode, result.StandardError.Trim());
                    throw new Exception($"MySQL command failed: {result.StandardError.Trim()}");
                }
                else
                {
                    logger.LogInformation("Successfully created or verified database {Schema}.", schema);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Critical error preparing database {Schema}", schema);
                throw;
            }
        }


        #endregion

        private async Task<(bool allSuccessful, int failedCount)> ProcessItemsWithDependenciesAsync(
       List<RestoreItem> restoreItems,
       Tuple<string, string, string> connectionString,
       string backupFolderPath,
       CancellationToken cancellationToken)
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
            logger.LogInformation("Schemas to process: {Schemas}", string.Join(", ", schemas));
            foreach (var schema in schemas)
            {
                logger.LogInformation("Preparing database for schema: {Schema}", schema);
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
                            bool ok = await ProcessItemWithRetriesAsync(item, connectionString, backupFolderPath, token);
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
                            bool ok = await ProcessItemWithRetriesAsync(item, connectionString, backupFolderPath, cancellationToken);
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



        private async Task<bool> ProcessItemWithRetriesAsync(
      RestoreItem item,
      Tuple<string, string, string> connectionString,
      string backupFolderPath,
      CancellationToken cancellationToken)
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
                    string filePath = GetRestoreFilePath(backupFolderPath, item);
                    if (!File.Exists(filePath))
                    {
                        logger.LogWarning("Item {ItemId} ({Schema}.{Name}): Restore file not found at {Path}",
                            item.Id, item.DatabaseItem.Schema, item.DatabaseItem.Name, filePath);
                        item.RetryCount++;
                        await UpdateRestoreItemStatusAsync(item, TaskStatus.Faulted, $"Restore file not found: {Path.GetFileName(filePath)}");
                        break;
                    }

                    success = await ExecuteRestoreForItemAsync(item, connectionString, backupFolderPath, cancellationToken);
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

        private string GetRestoreFilePath(string backupFolder, RestoreItem restoreItem)
        {
            string fileSuffix = restoreItem.DatabaseItem.Type switch
            {
                DatabaseItemType.TableStructure => "-structure.sql",
                DatabaseItemType.TableData => "-data.sql",
                DatabaseItemType.View => "-view.sql",
                DatabaseItemType.Trigger => "-triggers.sql",
                DatabaseItemType.Event => "-events.sql",
                DatabaseItemType.FunctionProcedure => "-funcs.sql",
                _ => throw new ArgumentOutOfRangeException(nameof(restoreItem.DatabaseItem.Type))
            };

            return Path.Combine(backupFolder, $"{restoreItem.DatabaseItem.Schema}.{restoreItem.DatabaseItem.Name}{fileSuffix}");
        }

        private async Task ProcessDecompressionAsync(
     CancellationToken cancellationToken,
     DirectoryInfo backupDirectoryInfo)
        {
            var decompSw = Stopwatch.StartNew();
            var compressedFiles = backupDirectoryInfo.GetFiles("*.xz").ToList();
            if (!compressedFiles.Any()) return;

            logger.LogInformation("Found {Count} compressed files to decompress.", compressedFiles.Count);

            var tasks = new List<Task>();
            int failedCount = 0;

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
                        Interlocked.Increment(ref failedCount);
                        logger.LogError(ex, "Decompression failed for {FileName}", compressedFile.Name);
                    }
                    finally
                    {
                        _restoreSemaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
            logger.LogInformation(
                "Decompression completed in {Elapsed}ms with {FailedCount} failures",
                decompSw.ElapsedMilliseconds,
                failedCount);

            if (failedCount > 0)
                throw new Exception($"Decompression failed for {failedCount} files. Cannot proceed with restore.");
        }

        private async Task ProcessCompressedFileAsync(
        FileInfo compressedFile,
        DirectoryInfo backupDirectoryInfo,
        CancellationToken ct)
        {
            var localStopwatch = Stopwatch.StartNew();
            try
            {
                logger.LogInformation("Decompressing file: {FileName}", compressedFile.Name);

                var command = $"7z e {compressedFile.FullName} -o{backupDirectoryInfo.FullName} -aoa";

                var result = await Cli.Wrap("/bin/bash")
                    .WithArguments(new[] { "-c", command })
                    .ExecuteBufferedAsync();

                if (result.ExitCode != 0)
                {
                    throw new Exception($"xz decompression failed for {compressedFile.Name}: {result.StandardError}");
                }

                // Delete the original .xz file after successful decompression
                File.Delete(compressedFile.FullName);
                logger.LogInformation(
                    "Decompressed {FileName} in {Ms}ms and removed source file",
                    compressedFile.Name,
                    localStopwatch.ElapsedMilliseconds);
            }
            finally
            {
                localStopwatch.Stop();
            }
        }


        private async Task ProcessEncryptedFileAsync(FileInfo encryptedFile, DirectoryInfo backupDirectoryInfo, CancellationToken ct)
        {
            var localStopwatch = Stopwatch.StartNew();
            try
            {
                logger.LogInformation("Decrypting file: {FileName}", encryptedFile.Name);
                var outputFile = encryptedFile.FullName.Replace(".enc", "");

                var command =
                    $"/usr/bin/openssl enc -d -aes-256-cbc -pbkdf2 -iter 20000 -in {encryptedFile.FullName} -out {encryptedFile.FullName.Replace(".enc", "")} -k {_encryptionKey}";

                var result = await Cli.Wrap("/bin/bash")
                    .WithArguments(new[] { "-c", command })
                    .ExecuteBufferedAsync();

                if (result.ExitCode != 0)
                {
                    logger.LogError("Decryption failed for {FileName}", encryptedFile.Name);
                }
                else
                {
                    var decryptedFile = new FileInfo(outputFile);
                    if (decryptedFile.Exists && decryptedFile.Length > 0)
                    {
                        File.Delete(encryptedFile.FullName);
                        logger.LogInformation("Successfully decrypted {FileName} in {Milliseconds}ms and removed source file",
                            encryptedFile.Name, localStopwatch.ElapsedMilliseconds);
                    }
                    else
                    {
                        logger.LogWarning("Decrypted output file is missing or empty: {FileName}", outputFile);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception decrypting file {FileName}", encryptedFile.Name);
                throw;
            }
            finally
            {
                localStopwatch.Stop();
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

        #region API Integration

        private async Task UpdateJobStatusAsync(RestoreJob job, TaskStatus status, string? message, CancellationToken ct)
        {
            try
            {
                job.Status = status;
                job.CompletedAt = status == TaskStatus.RanToCompletion ? DateTime.UtcNow : job.CompletedAt;
                job.StartedAt = job.StartedAt == default ? DateTime.UtcNow : job.StartedAt;

                var response = await _httpClient.PutAsJsonAsync($"jobs/restore/{job.Id}", job, ct);
                response.EnsureSuccessStatusCode();
                logger.LogInformation("Updated job {JobId} to {Status}", job.Id, status);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update job status");
                throw;
            }
        }

        private async Task GenerateRestoreJobsAsync(CancellationToken ct)
        {
            try
            {
                // Get all active restore plans
                var activePlans = await _httpClient
                    .GetFromJsonAsync<List<RestorePlan>>("plans/restore?isActive=true", ct)
                    ?? new List<RestorePlan>();

                foreach (var plan in activePlans)
                {
                    if (plan.ScheduleType == ScheduleType.Triggered)
                        continue;

                    var existingJobs = await _httpClient
                        .GetFromJsonAsync<List<RestoreJob>>($"jobs/restore?planId={plan.Id}", ct)
                        ?? new List<RestoreJob>();


                    var lastJob = existingJobs.OrderByDescending(j => j.CreatedAt).FirstOrDefault();
                    bool jobsExist = existingJobs.Any();

                    bool jobsStillRunning = existingJobs.Any(j => j.Status < TaskStatus.RanToCompletion);
                    bool lastJobFailed = lastJob != null && lastJob.Status == TaskStatus.Faulted;

                    if (jobsStillRunning || lastJobFailed)
                    {
                        string reason = lastJobFailed
                            ? $"last job {lastJob!.Id} failed"
                            : "jobs are still running";

                        logger.LogInformation("Skipping plan {PlanId} because {Reason}", plan.Id, reason);
                        continue;
                    }

                    // Handle based on schedule type
                    bool shouldCreateJob = false;

                    switch (plan.ScheduleType)
                    {
                        case ScheduleType.OneTime:
                            // For OneTime plans, create job only once
                            shouldCreateJob = !jobsExist;

                            if (jobsExist)
                            {
                                logger.LogInformation(
                                    "Skipping OneTime plan {PlanId} because it already ran at {LastRun}",
                                    plan.Id, lastJob!.CreatedAt);
                            }
                            break;

                        case ScheduleType.Repeating:
                            if (string.IsNullOrEmpty(plan.ScheduleCron))
                            {
                                logger.LogWarning("Repeating plan {PlanId} has no cron expression", plan.Id);
                                continue;
                            }

                            try
                            {
                                var cron = CronExpression.Parse(plan.ScheduleCron);
                                var nextRun = plan.NextRun ?? cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);

                                if (DateTime.UtcNow >= nextRun)
                                {
                                    shouldCreateJob = true;
                                    plan.NextRun = cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
                                }
                                else if (plan.NextRun == null)
                                {
                                    plan.NextRun = nextRun;
                                    await UpdateRestorePlan(plan, ct);
                                }
                            }
                            catch (CronFormatException ex)
                            {
                                logger.LogError(ex, "Invalid cron format for plan {PlanId}", plan.Id);
                                continue;
                            }
                            break;
                    }

                    if (shouldCreateJob)
                    {
                        if (await CreateRestoreJob(plan, ct))
                        {
                            plan.LastRun = DateTime.UtcNow;
                            await UpdateRestorePlan(plan, ct);
                            logger.LogInformation("Successfully created restore job for plan {PlanId}", plan.Id);
                        }
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
                var allBackupJobs = await _httpClient.GetFromJsonAsync<List<BackupJob>>("jobs/backup", ct);
                var backupJobs = allBackupJobs?
                    .Where(job => job.Status == TaskStatus.RanToCompletion && !job.HasTriggeredRestore)
                    .ToList();

                if (backupJobs == null || !backupJobs.Any())
                {
                    logger.LogInformation("No completed backup jobs pending restore trigger found");
                    return;
                }

                logger.LogInformation("Found {Count} completed backup jobs needing restore triggers", backupJobs.Count);

                foreach (var backup in backupJobs)
                {
                    logger.LogInformation("Processing backup job {BackupJobId} for restore triggers", backup.Id);

                    var plans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>(
                        $"plans/restore?sourceBackupPlanId={backup.BackupPlanId}&scheduleType=Triggered&isActive=true", ct)
                        ?? new List<RestorePlan>();

                    if (!plans.Any())
                    {
                        logger.LogInformation("No triggered restore plans found for backup {BackupJobId}", backup.Id);
                        continue;
                    }

                    bool anySuccess = false;
                    foreach (var plan in plans)
                    {
                        // Check existing restore jobs for this plan
                        var existingJobs = await _httpClient.GetFromJsonAsync<List<RestoreJob>>(
                            $"jobs/restore?planId={plan.Id}", ct) ?? new List<RestoreJob>();

                        var lastJob = existingJobs
                            .OrderByDescending(j => j.CreatedAt)
                            .FirstOrDefault();

                        // Combined skip condition
                        if ((lastJob != null && (lastJob.Status == TaskStatus.Faulted || lastJob.Status == TaskStatus.Canceled)) ||
                            existingJobs.Any(j => j.Status < TaskStatus.RanToCompletion))
                        {
                            string reason = lastJob != null && (lastJob.Status == TaskStatus.Faulted || lastJob.Status == TaskStatus.Canceled)
                                ? $"last job {lastJob.Id} is in {lastJob.Status} state"
                                : "active job exists";

                            logger.LogInformation("Skipping plan {PlanId} - {Reason}", plan.Id, reason);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(plan.ScheduleCron))
                        {
                            try
                            {
                                var cron = CronExpression.Parse(plan.ScheduleCron);

                                if (plan.LastRun.HasValue)
                                {
                                    var nextRun = plan.NextRun ?? cron.GetNextOccurrence(plan.LastRun.Value, TimeZoneInfo.Utc);

                                    if (DateTime.UtcNow < nextRun)
                                    {
                                        logger.LogInformation("Skipping plan {PlanId} - before scheduled time", plan.Id);
                                        continue;
                                    }
                                }
                            }
                            catch (CronFormatException ex)
                            {
                                logger.LogError(ex, "Invalid cron in plan {PlanId}", plan.Id);
                                continue;
                            }
                        }

                        logger.LogInformation("Triggering restore for plan {PlanId} (Backup: {BackupId})", plan.Id, backup.Id);

                        if (await CreateRestoreJob(plan, ct))
                        {
                            plan.LastRun = DateTime.UtcNow;
                            if (!string.IsNullOrEmpty(plan.ScheduleCron))
                            {
                                var cron = CronExpression.Parse(plan.ScheduleCron);
                                plan.NextRun = cron.GetNextOccurrence(plan.LastRun.Value, TimeZoneInfo.Utc);
                            }
                            await UpdateRestorePlan(plan, ct);

                            anySuccess = true;
                            logger.LogInformation("Successfully triggered restore job for plan {PlanId}", plan.Id);
                        }
                    }

                    if (anySuccess)
                    {
                        backup.HasTriggeredRestore = true;
                        await _httpClient.PutAsJsonAsync($"jobs/backup/{backup.Id}", backup, ct);
                        logger.LogInformation("Marked backup {BackupId} as having triggered restores", backup.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing completed backups");
            }
        }

    }
}
#endregion