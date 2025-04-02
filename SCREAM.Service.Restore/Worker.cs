using CliWrap;
using CliWrap.Buffered;
using CliWrap.Builders;
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
            string filePath = Path.Combine(_backupDirectory, $"{restoreItem.DatabaseItem.Schema}.{restoreItem.DatabaseItem.Name}.sql");

            if (!File.Exists(filePath))
            {
                logger.LogWarning("{Schema}.{Name}: Restore file not found; skipping restore.",
                    restoreItem.DatabaseItem.Schema, restoreItem.DatabaseItem.Name);
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

            // Append schema parameter based on DatabaseItem type
            switch (restoreItem.DatabaseItem.Type)
            {
                case DatabaseItemType.TableStructure:
                case DatabaseItemType.TableData:
                case DatabaseItemType.View:
                case DatabaseItemType.Trigger:
                case DatabaseItemType.Event:
                case DatabaseItemType.FunctionProcedure:
                    argsBuilder.Add(restoreItem.DatabaseItem.Schema);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(restoreItem.DatabaseItem.Type), "Unsupported database item type");
            }

            var args = argsBuilder.Build();

            try
            {
                logger.LogInformation("Starting restore for {Schema}.{Name} using file: {FilePath}",
                    restoreItem.DatabaseItem.Schema, restoreItem.DatabaseItem.Name, filePath);

                var result = await Cli.Wrap("/usr/bin/mysql")
                    .WithArguments(args)
                    .WithStandardInputPipe(PipeSource.FromFile(filePath))
                    .ExecuteBufferedAsync(ct);

                if (result.ExitCode != 0)
                {
                    logger.LogError("MySQL restore failed for {Schema}.{Name}: {Error}",
                        restoreItem.DatabaseItem.Schema, restoreItem.DatabaseItem.Name, result.StandardError);
                    await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.Faulted, result.StandardError);
                    return false;
                }

                logger.LogInformation("Successfully restored {Schema}.{Name}",
                    restoreItem.DatabaseItem.Schema, restoreItem.DatabaseItem.Name);
                await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.RanToCompletion);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception restoring {Schema}.{Name}: {Error}",
                    restoreItem.DatabaseItem.Schema, restoreItem.DatabaseItem.Name, ex.Message);
                await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.Faulted, ex.Message);
                return false;
            }
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

        #region File Processing

        private async Task ProcessRestoreFilesAsync(CancellationToken cancellationToken)
        {
            var backupDirectoryInfo = new DirectoryInfo(_backupDirectory);
            logger.LogInformation("Found {FileCount} files in directory: {Directory}", backupDirectoryInfo.GetFiles("*").Length, backupDirectoryInfo.FullName);

            // Process decryption and decompression first.
            await ProcessDecryptionAsync(cancellationToken, backupDirectoryInfo);
            await ProcessDecompressionAsync(cancellationToken, backupDirectoryInfo);

            var stopwatch = Stopwatch.StartNew();
            var restoreItems = await GetRestoreItemsFromApiAsync(cancellationToken);
            var sqlFiles = backupDirectoryInfo.GetFiles("*.sql");
            logger.LogInformation("Found {FileCount} SQL files in directory", sqlFiles.Length);
            logger.LogInformation("Running cleanup commands on the SQL files...");

            // Ensure target databases exist.
            foreach (var group in restoreItems.GroupBy(item => item.DatabaseItem.Schema))
            {
                logger.LogInformation("Schema {Schema}: Creating database if not exists...", group.Key);
                try
                {
                    await Cli.Wrap("/usr/bin/mysql")
                        .WithArguments(args => args
                            .Add($"--host={_mysqlHost}")
                            .Add($"--user={_mysqlUser}")
                            .Add($"--password={_mysqlPassword}")
                            .Add($"--execute=\"CREATE DATABASE IF NOT EXISTS {group.Key};\"", false)
                        )
                        .ExecuteBufferedAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create database for schema {Schema}", group.Key);
                }
            }

            int retryCount = 0;
            while (restoreItems.Any(item => item.Status != TaskStatus.RanToCompletion))
            {
                logger.LogInformation("Restoring pending files (Retry attempt {RetryCount})...", retryCount);

                var pendingItems = restoreItems.Where(item => item.Status == TaskStatus.WaitingToRun || item.Status == TaskStatus.Faulted).ToList();

                if (pendingItems.Count == 0)
                {
                    logger.LogInformation("No items left to restore. Exiting loop.");
                    break;
                }

                logger.LogInformation("Processing {PendingCount} pending restore items.", pendingItems.Count);

                // Process each item sequentially to avoid deadlocks
                foreach (var item in pendingItems)
                {
                    await ExecuteRestoreForItemAsync(item, Tuple.Create(_mysqlHost, _mysqlUser, _mysqlPassword), cancellationToken);
                }

                // Check for failed items
                var failedItems = restoreItems.Where(item => item.Status == TaskStatus.Faulted).ToList();
                if (failedItems.Count > 0)
                {
                    if (retryCount < _maxRetries)
                    {
                        // Exponential backoff delay
                        int delaySeconds = (int)Math.Pow(2, retryCount);
                        logger.LogInformation("Waiting {DelaySeconds} seconds before retry...", delaySeconds);
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

                        retryCount++;
                        logger.LogWarning("Retrying {FailedCount} failed items (Attempt {RetryCount})", failedItems.Count, retryCount);

                        foreach (var item in failedItems)
                        {
                            await UpdateRestoreItemStatusAsync(item, TaskStatus.WaitingToRun);
                        }
                    }
                    else
                    {
                        logger.LogError("Max retries reached. {FailedCount} restore items permanently failed.", failedItems.Count);
                        break;
                    }
                }
            }
        }

        private async Task ProcessDecompressionAsync(CancellationToken cancellationToken, DirectoryInfo backupDirectoryInfo)
        {
            // Start stopwatch for overall decompression timing.
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
            // Start stopwatch to time the decompression for this individual file.
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

        #endregion

        #region API Integration

        private async Task<List<RestoreItem>> GetRestoreItemsFromApiAsync(CancellationToken ct)
        {
            try
            {
                var activeJobs = await _httpClient.GetFromJsonAsync<List<RestoreJob>>($"jobs/restore?status={TaskStatus.Created}", ct);
                if (activeJobs == null || activeJobs.Count == 0)
                    return new List<RestoreItem>();

                var activeJob = activeJobs.First();
                var updatedJob = new RestoreJob
                {
                    Id = activeJob.Id,
                    Status = TaskStatus.Running,
                    IsCompressed = activeJob.IsCompressed,
                    IsEncrypted = activeJob.IsEncrypted,
                    RestorePlanId = activeJob.RestorePlanId,
                    StartedAt = activeJob.StartedAt != default ? activeJob.StartedAt : DateTime.UtcNow,
                    CreatedAt = activeJob.CreatedAt
                };

                var response = await _httpClient.PutAsJsonAsync($"jobs/restore/{activeJob.Id}", updatedJob, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(ct);
                    logger.LogWarning("Failed to update restore job {JobId} status: {StatusCode}. Response: {ResponseBody}",
                        activeJob.Id, response.StatusCode, responseBody);
                    response.EnsureSuccessStatusCode();
                }

                var items = await _httpClient.GetFromJsonAsync<List<RestoreItem>>($"jobs/restore/items/{activeJob.Id}", ct);
                return items ?? new List<RestoreItem>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve restore items: {Message}", ex.Message);
                return new List<RestoreItem>();
            }
        }

        private async Task GenerateRestoreJobsAsync(CancellationToken ct)
        {
            try
            {
                var eligiblePlans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>(
                    "plans/restore?isActive=true&excludeTriggered=true&nextRunIsNull=true", ct);
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

                // Process only backup jobs that completed and haven't been processed for restore yet.
                var completedJobs = backupJobs
                    .Where(job => job.Status == TaskStatus.RanToCompletion && !job.HasTriggeredRestore)
                    .ToList();

                foreach (var backupJob in completedJobs)
                {
                    logger.LogInformation("Processing backup job {BackupJobId} for backup plan {BackupPlanId}", backupJob.Id, backupJob.BackupPlanId);

                    // Query restore plans with schedule type "Triggered"
                    var restorePlans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>(
                        $"plans/restore?sourceBackupPlanId={backupJob.BackupPlanId}&scheduleType=Triggered", ct);

                    // If there are no restore plans with schedule type "Triggered", skip this backup job.
                    if (restorePlans == null || !restorePlans.Any())
                    {
                        logger.LogInformation("Backup job {BackupJobId} does not have any restore plans with schedule type 'Triggered'. Skipping.", backupJob.Id);
                        continue;
                    }

                    foreach (var plan in restorePlans)
                    {
                        var response = await _httpClient.PostAsync($"plans/restore/{plan.Id}/run", null, ct);
                        if (response.IsSuccessStatusCode)
                        {
                            logger.LogInformation("Created restore job for restore plan {PlanId}", plan.Id);
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync(ct);
                            logger.LogError("Failed to create restore job for plan {PlanId}: {Error}", plan.Id, error);
                        }
                    }

                    // Mark backup job as processed only if at least one restore plan was processed.
                    backupJob.HasTriggeredRestore = true;
                    await _httpClient.PutAsJsonAsync($"jobs/backup/{backupJob.Id}", backupJob, ct);
                    logger.LogInformation("Backup job {BackupJobId} marked as having triggered a restore.", backupJob.Id);
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