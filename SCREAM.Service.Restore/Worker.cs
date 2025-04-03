using CliWrap;
using CliWrap.Buffered;
using CliWrap.Builders;
using Cronos;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Restore;
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

            logger.LogInformation("Configuration loaded: Host={Host}, User={User}", _mysqlHost, _mysqlUser);
        }

        #region Restore Execution

        private async Task<bool> ExecuteRestoreForItemAsync(RestoreItem restoreItem, Tuple<string, string, string> connectionString, CancellationToken ct)
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

            string filePath = Path.Combine(_backupDirectory,
                $"{restoreItem.DatabaseItem.Schema}.{restoreItem.DatabaseItem.Name}{fileSuffix}");

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
            var stopwatch = Stopwatch.StartNew();
            var backupDirectoryInfo = new DirectoryInfo(_backupDirectory);
            int totalFiles = backupDirectoryInfo.GetFiles("*").Length;
            logger.LogInformation("Found {FileCount} files in directory: {Directory}", totalFiles, backupDirectoryInfo.FullName);

            try
            {
                await ProcessDecryptionAsync(cancellationToken, backupDirectoryInfo);
                await ProcessDecompressionAsync(cancellationToken, backupDirectoryInfo);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing decryption/decompression.");
            }

            var sqlFiles = backupDirectoryInfo.GetFiles("*.sql");
            logger.LogInformation("Found {FileCount} SQL files in directory", sqlFiles.Length);
            logger.LogInformation("Running cleanup commands on the SQL files...");

            var (jobId, restoreItems) = await GetRestoreItemsFromApiAsync(cancellationToken);
            if (jobId == 0 || !restoreItems.Any())
            {
                logger.LogInformation("No active restore job and items to process.");
                return;
            }

            await PrepareTargetDatabasesAsync(restoreItems, cancellationToken);

            var connectionString = Tuple.Create(_mysqlHost, _mysqlUser, _mysqlPassword);

            bool allSuccessful = await ProcessItemsByTypeAsync(restoreItems, connectionString, cancellationToken);

            stopwatch.Stop();
            logger.LogInformation("Total Process took {Minutes} minutes.", stopwatch.Elapsed.TotalMinutes);

            if (!allSuccessful)
            {
                await UpdateJobStatusAsync(jobId, TaskStatus.Faulted, "Some items failed after maximum retry attempts");
            }
            else
            {
                await UpdateJobStatusAsync(jobId, TaskStatus.RanToCompletion, "All items restored successfully");
            }
        }

        #endregion

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

        private async Task<bool> ProcessItemsByTypeAsync(List<RestoreItem> initialItems, Tuple<string, string, string> connectionString, CancellationToken cancellationToken)
        {
            bool allSuccessful = true;

            var itemTypes = new[]
            {
                DatabaseItemType.TableStructure,
                DatabaseItemType.TableData,
                DatabaseItemType.FunctionProcedure,
                DatabaseItemType.View
            };

            foreach (var type in itemTypes)
            {
                var itemsToProcess = initialItems.Where(item =>
                    item.DatabaseItem.Type == type &&
                    (item.Status == TaskStatus.WaitingToRun || item.Status == TaskStatus.Faulted) &&
                    item.RetryCount < _maxRetries).ToList();

                if (!itemsToProcess.Any()) continue;

                logger.LogInformation("Processing {Count} items of type {Type}", itemsToProcess.Count, type);

                foreach (var item in itemsToProcess)
                {
                    bool itemSuccess = await ProcessItemWithRetriesAsync(item, connectionString, cancellationToken);
                    if (!itemSuccess) allSuccessful = false;
                }
            }

            return allSuccessful;
        }

        private async Task<bool> ProcessItemWithRetriesAsync(RestoreItem item, Tuple<string, string, string> connectionString, CancellationToken cancellationToken)
        {
            int attempt = 0;
            bool success = false;

            while (attempt <= _maxRetries && !success)
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
                        logger.LogWarning("Task was canceled during retry delay");
                        break;
                    }
                }

                try
                {
                    success = await ExecuteRestoreForItemAsync(item, connectionString, cancellationToken);
                    if (success) break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Exception executing restore for item {ItemId} on attempt {Attempt}",
                        item.Id, attempt + 1);

                    item.RetryCount++;
                    await UpdateRestoreItemStatusAsync(item, TaskStatus.Faulted, ex.Message);
                }

                attempt++;
            }

            // If still failed after all retries, log a warning
            if (!success && item.RetryCount >= _maxRetries)
            {
                logger.LogWarning("Item {ItemId} ({Schema}.{Name}) failed after maximum retry attempts",
                    item.Id, item.DatabaseItem.Schema, item.DatabaseItem.Name);
            }

            return success;
        }

        private async Task ProcessDecompressionAsync(CancellationToken cancellationToken, DirectoryInfo backupDirectoryInfo)
        {
            var decompSw = Stopwatch.StartNew();

            var compressedFiles = backupDirectoryInfo.GetFiles().Where(file => file.Extension == ".xz").ToList();
            if (!compressedFiles.Any())
                return;

            logger.LogInformation("Found {Count} compressed files to decompress.", compressedFiles.Count);

            var tasks = new List<Task>();
            var semaphore = new SemaphoreSlim(_maxRetries);

            foreach (var compressedFile in compressedFiles)
            {
                await semaphore.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessCompressedFileAsync(compressedFile, backupDirectoryInfo, cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
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
            var semaphore = new SemaphoreSlim(_maxRetries);

            foreach (var encryptedFile in encryptedFiles)
            {
                await semaphore.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessEncryptedFileAsync(encryptedFile, backupDirectoryInfo, cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
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
                    return (0, new List<RestoreItem>());

                var activeJob = activeJobs.First();

                if (activeJob.Status == TaskStatus.Created)
                {
                    await UpdateJobStatusAsync(activeJob.Id, TaskStatus.Running);
                }

                var items = await _httpClient.GetFromJsonAsync<List<RestoreItem>>($"jobs/restore/items/{activeJob.Id}", ct);
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
                var response = await _httpClient.GetFromJsonAsync<RestoreJob>($"jobs/restore/{jobId}");
                if (response == null)
                {
                    logger.LogWarning("Restore job {JobId} not found to update status", jobId);
                    return false;
                }

                var activeJob = response;

                var updatedJob = new RestoreJob
                {
                    Id = activeJob.Id,
                    Status = status,
                    IsCompressed = activeJob.IsCompressed,
                    IsEncrypted = activeJob.IsEncrypted,
                    RestorePlanId = activeJob.RestorePlanId,
                    StartedAt = activeJob.StartedAt != default ? activeJob.StartedAt : DateTime.UtcNow,
                    CompletedAt = status == TaskStatus.RanToCompletion || status == TaskStatus.Faulted ? DateTime.UtcNow : default,
                    CreatedAt = activeJob.CreatedAt
                };

                var putResponse = await _httpClient.PutAsJsonAsync($"jobs/restore/{activeJob.Id}", updatedJob);
                if (!putResponse.IsSuccessStatusCode)
                {
                    var responseBody = await putResponse.Content.ReadAsStringAsync();
                    logger.LogWarning("Failed to update restore job {JobId} status to {Status}: {StatusCode}. Response: {ResponseBody}",
                        activeJob.Id, status, putResponse.StatusCode, responseBody);
                    return false;
                }

                logger.LogInformation("Successfully updated restore job {JobId} status to {Status}", activeJob.Id, status);
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
                var eligiblePlans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>("plans/restore?isActive=true&excludeTriggered=true&nextRunIsNull=true", ct);
                if (eligiblePlans == null || eligiblePlans.Count == 0)
                {
                    logger.LogWarning("No eligible restore plans received from API.");
                    return;
                }

                foreach (var plan in eligiblePlans)
                {
                    var activeJobs = await _httpClient.GetFromJsonAsync<List<RestoreJob>>($"jobs/restore?planId={plan.Id}", ct);
                    if (activeJobs != null && activeJobs.Any(job => job.Status >= TaskStatus.Created && job.Status < TaskStatus.RanToCompletion))
                        continue;

                    var nextRun = plan.GetNextRun(DateTime.UtcNow);
                    if (nextRun != null)
                    {
                        plan.NextRun = nextRun;
                        var updateResponse = await _httpClient.PostAsJsonAsync("plans/restore", plan, ct);
                        if (updateResponse.IsSuccessStatusCode)
                        {
                            logger.LogInformation("Restore plan {PlanId} updated with NextRun {NextRun}", plan.Id, plan.NextRun);
                        }
                        else
                        {
                            var error = await updateResponse.Content.ReadAsStringAsync(ct);
                            logger.LogError("Error updating restore plan {PlanId}: {Error}", plan.Id, error);
                        }
                    }

                    try
                    {
                        var response = await _httpClient.PostAsJsonAsync($"plans/restore/{plan.Id}/run", new { }, ct);
                        if (response.IsSuccessStatusCode)
                        {
                            logger.LogInformation("Created restore job for restore plan {PlanId}", plan.Id);
                        }
                        else
                        {
                            var responseContent = await response.Content.ReadAsStringAsync(ct);
                            logger.LogError("Failed to create restore job for plan {PlanId}. Status: {StatusCode}, Content: {Content}",
                                plan.Id, response.StatusCode, responseContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Exception occurred while creating restore job for plan {PlanId}", plan.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating restore jobs via API.");
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
                    logger.LogInformation("Processing backup job {BackupJobId} for backup plan {BackupPlanId}",
                        backupJob.Id, backupJob.BackupPlanId);

                    var restorePlans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>(
                        $"plans/restore?sourceBackupPlanId={backupJob.BackupPlanId}&scheduleType=Triggered", ct);

                    if (restorePlans == null || !restorePlans.Any())
                    {
                        logger.LogInformation("No triggered restore plans found for backup job {BackupJobId}", backupJob.Id);
                        continue;
                    }

                    foreach (var plan in restorePlans)
                    {
                        var cronExpression = CronExpression.Parse(plan.ScheduleCron);
                        var baseTime = plan.LastRun ?? backupJob.CompletedAt ?? DateTime.UtcNow;
                        var nextRun = cronExpression.GetNextOccurrence(baseTime);

                        if (nextRun.HasValue && nextRun <= DateTime.UtcNow)
                        {
                            logger.LogInformation("Triggering restore for plan {PlanId} based on cron schedule {Cron}",
                                plan.Id, plan.ScheduleCron);

                            var response = await _httpClient.PostAsJsonAsync(
                                $"plans/restore/{plan.Id}/run", new { }, ct);

                            if (response.IsSuccessStatusCode)
                            {
                                logger.LogInformation("Created restore job for restore plan {PlanId}", plan.Id);

                                // Build updated plan with ALL required fields
                                var updatedPlan = new RestorePlan
                                {
                                    Id = plan.Id,
                                    Name = plan.Name,
                                    Description = plan.Description,
                                    DatabaseTargetId = plan.DatabaseTargetId,
                                    SourceBackupPlanId = plan.SourceBackupPlanId,
                                    ScheduleCron = plan.ScheduleCron,
                                    ScheduleType = plan.ScheduleType,
                                    IsActive = plan.IsActive,
                                    OverwriteExisting = plan.OverwriteExisting,
                                    LastRun = DateTime.UtcNow,
                                    NextRun = cronExpression.GetNextOccurrence(DateTime.UtcNow),
                                    Items = plan.Items.ToList()
                                };

                                var updateResponse = await _httpClient.PostAsJsonAsync(
                                    "plans/restore", updatedPlan, ct);

                                if (!updateResponse.IsSuccessStatusCode)
                                {
                                    var error = await updateResponse.Content.ReadAsStringAsync(ct);
                                    logger.LogError("Failed to update restore plan {PlanId}: {Error}", plan.Id, error);
                                }
                            }
                            else
                            {
                                var error = await response.Content.ReadAsStringAsync(ct);
                                logger.LogError("Failed to create restore job for plan {PlanId}: {Error}", plan.Id, error);
                            }
                        }
                    }

                    // Mark backup job as processed
                    backupJob.HasTriggeredRestore = true;
                    await _httpClient.PutAsJsonAsync($"jobs/backup/{backupJob.Id}", backupJob, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing completed backup jobs via API.");
            }
        }

        #endregion
    }
}
