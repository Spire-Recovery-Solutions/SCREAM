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

        private readonly ILogger<Worker> _logger = logger;
        private readonly HttpClient _httpClient = httpClientFactory.CreateClient("SCREAM");
        private readonly IConfiguration _configuration = configuration;
        private string? _encryptionKey;
        private int _maxRetries = 3;
        private readonly string _backupRootPath = "/backups";

        private string _hostName = "";
        private string _userName = "";
        private string _password = "";

        private string GetConfigValue(string envKey, string configKey, string defaultValue = "")
        {
            var value = Environment.GetEnvironmentVariable(envKey);
            return !string.IsNullOrEmpty(value) ? value : _configuration[configKey] ?? defaultValue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LoadConfiguration();
            Directory.CreateDirectory(_backupRootPath);
            _logger.LogInformation("Verified backup directory exists at: {path}", _backupRootPath);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Restore Worker cycle started at: {time}", DateTimeOffset.Now);

                    await ProcessCompletedBackupJobs(stoppingToken);
                    await GenerateRestoreJobs(stoppingToken);

                    await ProcessRestoreFiles(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Critical error in main execution loop");
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        private void LoadConfiguration()
        {
            _encryptionKey = GetConfigValue("MYSQL_BACKUP_ENCRYPTION_KEY", "MySqlBackup:EncryptionKey");

            // Load connection parameters
            _hostName = GetConfigValue("MYSQL_BACKUP_HOSTNAME", "MySqlBackup:HostName");
            _userName = GetConfigValue("MYSQL_BACKUP_USERNAME", "MySqlBackup:UserName");
            _password = GetConfigValue("MYSQL_BACKUP_PASSWORD", "MySqlBackup:Password");

        }

        private async Task<bool> ExecuteRestoreForItemAsync(RestoreItem restoreItem, Tuple<string, string, string> connectionString, CancellationToken ct)
        {
            string filePath = Path.Combine(_backupRootPath, $"{restoreItem.DatabaseItem.Schema}.{restoreItem.DatabaseItem.Name}.sql");

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("{Schema}.{Name}: Restore file not found; skipping restore.",
                    restoreItem.DatabaseItem.Schema, restoreItem.DatabaseItem.Name);
                return false;
            }
            
            await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.Running);
            var argsBuilder = new ArgumentsBuilder()
                .Add("--max_allowed_packet=1073741824")
                .Add($"--host={connectionString.Item1}")
                .Add($"--user={connectionString.Item2}")
                .Add($"--password={connectionString.Item3}")
                .Add("--init-command=\"SET SESSION innodb_strict_mode=OFF;\"", false);

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
                    throw new ArgumentOutOfRangeException();
            }

            var args = argsBuilder.Build();

            try
            {
                var result = await Cli.Wrap("/usr/bin/mysql")
                    .WithArguments(args)
                    .WithStandardInputPipe(PipeSource.FromFile(filePath))
                    .ExecuteBufferedAsync(ct);

                if (result.ExitCode != 0)
                {
                    _logger.LogError("MySQL restore failed for {Schema}.{Name}: {Error}",
                        restoreItem.DatabaseItem.Schema, restoreItem.DatabaseItem.Name, result.StandardError);

                    await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.Faulted, result.StandardError);
                    return false;
                }

                _logger.LogInformation("Successfully restored {Schema}.{Name}",
                    restoreItem.DatabaseItem.Schema, restoreItem.DatabaseItem.Name);

                await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.RanToCompletion);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception restoring {Schema}.{Name}: {Error}",
                    restoreItem.DatabaseItem.Schema, restoreItem.DatabaseItem.Name, ex.Message);

                await UpdateRestoreItemStatusAsync(restoreItem, TaskStatus.Faulted, ex.Message);
                return false;
            }
        }

        private async Task UpdateRestoreItemStatusAsync(RestoreItem restoreItem, TaskStatus status, string errorMessage = null)
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
                    _logger.LogWarning("Failed to update restore item {ItemId} status: {StatusCode}. Response: {ResponseBody}",
                        restoreItem.Id, response.StatusCode, responseBody);

                    response.EnsureSuccessStatusCode();
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed when updating restore item {ItemId} status", restoreItem.Id);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception updating restore item {ItemId} status", restoreItem.Id);
                throw;
            }
        }

        private async Task ProcessRestoreFiles(CancellationToken stoppingToken)
        {
            DirectoryInfo directoryInfo = new(_backupRootPath);
            _logger.LogInformation("Found {fileCount} files in directory: {path}",
            directoryInfo.GetFiles("*").Length, directoryInfo.FullName);

            var tasks = new List<Task>();
            var semaphore = new SemaphoreSlim(_maxRetries);

            await DecryptFiles(stoppingToken, directoryInfo, semaphore, tasks);
            await DecompressFiles(stoppingToken, directoryInfo, semaphore, tasks);

            var sw = Stopwatch.StartNew();

            var restoreItems = await GetRestoreItemsFromApiAsync(stoppingToken);
            var files = directoryInfo.GetFiles("*.sql");
            _logger.LogInformation("Found {fileCount} SQL files in directory...", files.Length);

            _logger.LogInformation("Running cleanup commands on the SQL Files...");

            // Ensure target databases exist
            foreach (var group in restoreItems.GroupBy(r => r.DatabaseItem.Schema))
            {
                _logger.LogInformation("Schema {schemaName}: Creating Database if not exist...", group.Key);
                await Cli.Wrap("/usr/bin/mysql")
                    .WithArguments(args => args
                        .Add($"--host={_hostName}")
                        .Add($"--user={_userName}")
                        .Add($"--password={_password}")
                        .Add($"--execute=\"CREATE DATABASE IF NOT EXISTS {group.Key};\"", false)
                    )
                    .ExecuteBufferedAsync(stoppingToken);
            }

            int retryCount = 0;
            while (restoreItems.Any(r => r.Status != TaskStatus.RanToCompletion))
            {
                _logger.LogInformation("Restoring pending files...");

                var pendingItems = restoreItems.Where(r => r.Status == TaskStatus.WaitingToRun).ToList();
                var restoreTasks = pendingItems.Select(item =>
                    ExecuteRestoreForItemAsync(
                        item,
                        new Tuple<string, string, string>(_hostName, _userName, _password),
                        stoppingToken
                    )
                ).ToList();

                await Task.WhenAll(restoreTasks);

                var failedItems = restoreItems.Where(r => r.Status == TaskStatus.Faulted).ToList();
                if (failedItems.Count > 0 && retryCount < _maxRetries)
                {
                    retryCount++;
                    _logger.LogWarning("Retrying {count} failed items (Attempt {retryCount})", failedItems.Count, retryCount);

                    foreach (var item in failedItems)
                    {
                        await UpdateRestoreItemStatusAsync(item, TaskStatus.Created);
                    }
                }
                else
                {
                    if (failedItems.Count > 0)
                    {
                        _logger.LogError("Max retries reached. {count} restore items permanently failed.", failedItems.Count);
                    }
                    break;
                }
            }

            sw.Stop();
            _logger.LogInformation("Total Restore Process: {minutes} minutes.", sw.Elapsed.TotalMinutes);
        }

        private async Task DecompressFiles(CancellationToken stoppingToken, DirectoryInfo directoryInfo, SemaphoreSlim semaphore, List<Task> tasks)
        {
            var decompSw = Stopwatch.StartNew();
            if (directoryInfo.GetFiles().Any(f => f.Extension == ".xz"))
            {
                foreach (var compressedFile in directoryInfo.GetFiles().Where(f => f.Extension == ".xz"))
                {
                    await semaphore.WaitAsync(stoppingToken);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessCompressedFileAsync(compressedFile);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, stoppingToken));
                }
                await Task.WhenAll(tasks);

                async Task ProcessCompressedFileAsync(FileInfo compressedFile)
                {
                    var localSw = Stopwatch.StartNew();
                    var command = $"7z e {compressedFile.FullName} -o{directoryInfo.FullName} -aoa";
                    var result = await Cli.Wrap("/bin/bash")
                        .WithArguments(new[] { "-c", command })
                        .ExecuteBufferedAsync();
                    File.Delete(compressedFile.FullName);
                    localSw.Stop();
                    _logger.LogInformation("Took {ms}ms to decompress {file}", localSw.ElapsedMilliseconds, compressedFile.Name);
                }

                decompSw.Stop();
                _logger.LogInformation("Took {ms}ms to decompress all files", decompSw.ElapsedMilliseconds);
                Thread.Sleep(10000);
            }
            tasks.Clear();
        }

        private async Task DecryptFiles(CancellationToken stoppingToken, DirectoryInfo directoryInfo, SemaphoreSlim semaphore, List<Task> tasks)
        {
            var decryptSw = Stopwatch.StartNew();
            if (directoryInfo.GetFiles().Any(f => f.Extension == ".enc"))
            {
                foreach (var encryptedFile in directoryInfo.GetFiles().Where(f => f.Extension == ".enc"))
                {
                    await semaphore.WaitAsync(stoppingToken);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessEncryptedFileAsync(encryptedFile);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, stoppingToken));
                }
                await Task.WhenAll(tasks);

                async Task ProcessEncryptedFileAsync(FileInfo encryptedFile)
                {
                    var localSw = Stopwatch.StartNew();
                    var command = $"/usr/bin/openssl enc -d -aes-256-cbc -pbkdf2 -iter 20000 -in {encryptedFile.FullName} -out {encryptedFile.FullName.Replace(".enc", "")} -k {_encryptionKey}";
                    var result = await Cli.Wrap("/bin/bash")
                        .WithArguments(new[] { "-c", command })
                        .ExecuteBufferedAsync();
                    File.Delete(encryptedFile.FullName);
                    localSw.Stop();
                    _logger.LogInformation("Took {ms}ms to decrypt {file}", localSw.ElapsedMilliseconds, encryptedFile.Name);
                }

                decryptSw.Stop();
                _logger.LogInformation("Took {ms}ms to decrypt all files", decryptSw.ElapsedMilliseconds);
                Thread.Sleep(10000);
            }
            tasks.Clear();
        }

        private async Task ProcessCompletedBackupJobs(CancellationToken stoppingToken)
        {
            try
            {
                // Retrieve backup jobs from the API.
                var backupJobs = await _httpClient.GetFromJsonAsync<List<BackupJob>>("jobs/backup", stoppingToken);
                if (backupJobs is null)
                {
                    _logger.LogWarning("No backup jobs received from API.");
                    return;
                }

                // Filter backup jobs that have RanToCompletion and haven't triggered a restore.
                var completedJobs = backupJobs
                    .Where(j => j.Status == TaskStatus.RanToCompletion && !j.HasTriggeredRestore)
                    .ToList();

                foreach (var backupJob in completedJobs)
                {
                    // Retrieve all restore plans from the API.
                    var restorePlans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>($"plans/restore?sourceBackupPlanId={backupJob.BackupPlanId}&scheduleType=Triggered", stoppingToken);


                    if (restorePlans is null)
                        continue;

                    foreach (var restorePlan in restorePlans)
                    {
                        // Create a new restore job for this restore plan.
                        var response = await _httpClient.PostAsync(
                            $"plans/restore/{restorePlan.Id}/run", null, stoppingToken);

                        if (response.IsSuccessStatusCode)
                        {
                            _logger.LogInformation("Created restore job for restore plan {RestorePlanId}",
                                restorePlan.Id);
                        }
                        else
                        {
                            var error = await response.Content.ReadAsStringAsync(stoppingToken);
                            _logger.LogError("Failed to create restore job for plan {RestorePlanId}: {Error}",
                                restorePlan.Id, error);
                        }
                    }

                    // Mark backup job as having triggered a restore.
                    backupJob.HasTriggeredRestore = true;
                    // Optionally update the backup job via API (if you have an endpoint for that):
                    await _httpClient.PutAsJsonAsync($"jobs/backup/{backupJob.Id}", backupJob, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing completed backup jobs via API.");
            }
        }

        private async Task<List<RestoreItem>> GetRestoreItemsFromApiAsync(CancellationToken ct)
        {
            try
            {
                var activeJobs =
                    await _httpClient.GetFromJsonAsync<List<RestoreJob>>($"jobs/restore?status={TaskStatus.Created}", ct);

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
                    _logger.LogWarning("Failed to update restore job {JobId} status: {StatusCode}. Response: {ResponseBody}",
                        activeJob.Id, response.StatusCode, responseBody);

                    response.EnsureSuccessStatusCode();
                }

                var items = await _httpClient.GetFromJsonAsync<List<RestoreItem>>($"jobs/restore/items/{activeJob.Id}", ct);
                return items ?? new List<RestoreItem>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve restore items: {Message}", ex.Message);
                return new List<RestoreItem>();
            }
        }

        private async Task GenerateRestoreJobs(CancellationToken stoppingToken)
        {
            try
            {
                // Retrieve only eligible restore plans via API filtering.
                var eligiblePlans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>(
                    "plans/restore?isActive=true&excludeTriggered=true&nextRunIsNull=true", stoppingToken);
                if (eligiblePlans is null || eligiblePlans.Count == 0)
                {
                    _logger.LogWarning("No eligible restore plans received from API.");
                    return;
                }

                foreach (var restorePlan in eligiblePlans)
                {
                    // Retrieve active restore jobs for this plan via a separate API call.
                    var activeJobs =
                        await _httpClient.GetFromJsonAsync<List<RestoreJob>>($"jobs/restore?planId={restorePlan.Id}",
                            stoppingToken);


                    if (activeJobs != null && activeJobs.Any(j =>
                            j.Status >= TaskStatus.Created && j.Status < TaskStatus.RanToCompletion))
                    {
                        continue;
                    }

                    // Calculate NextRun using the plan's logic.
                    var nextRun = restorePlan.GetNextRun(DateTime.UtcNow);
                    if (nextRun != null)
                    {
                        restorePlan.NextRun = nextRun;
                        // Update the restore plan via API using POST (which handles both creation and update).
                        var updateResponse =
                            await _httpClient.PostAsJsonAsync("plans/restore", restorePlan, stoppingToken);
                        if (updateResponse.IsSuccessStatusCode)
                        {
                            _logger.LogInformation("Restore plan {RestorePlanId} updated with NextRun {NextRun}",
                                restorePlan.Id, restorePlan.NextRun);
                        }
                        else
                        {
                            var error = await updateResponse.Content.ReadAsStringAsync(stoppingToken);
                            _logger.LogError("Error updating restore plan {RestorePlanId}: {Error}", restorePlan.Id,
                                error);
                        }
                    }

                    try
                    {
                        var response = await _httpClient.PostAsJsonAsync($"plans/restore/{restorePlan.Id}/run", new { },
                            stoppingToken);

                        if (response.IsSuccessStatusCode)
                        {
                            _logger.LogInformation("Created restore job for restore plan {RestorePlanId}",
                                restorePlan.Id);
                        }
                        else
                        {
                            var responseContent = await response.Content.ReadAsStringAsync(stoppingToken);
                            _logger.LogError(
                                "Failed to create restore job for plan {RestorePlanId}. Status: {StatusCode}, Content: {Content}",
                                restorePlan.Id, response.StatusCode, responseContent);
                        }

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception occurred while creating restore job for plan {RestorePlanId}",
                            restorePlan.Id);
                    }
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating restore jobs via API.");
            }
        }
    }
}