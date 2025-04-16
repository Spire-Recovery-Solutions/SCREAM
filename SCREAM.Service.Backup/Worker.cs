using CliWrap;
using Cronos;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data.Entities.StorageTargets;
using SCREAM.Data.Enums;
using SCREAM.Service.Backup;
using System.Diagnostics;
using System.Net.Http.Json;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly PeriodicTimer _timer;
    private readonly string _hostName;
    private readonly string _userName;
    private readonly string _password;
    private readonly string _encryptionKey;
    private readonly string _backupFolder;
    private readonly string _maxPacketSize;
    private readonly IConfiguration _configuration;
    private readonly int _threads;
    private readonly HttpClient _httpClient;
    private readonly int _maxRetries = 3;

    public Worker(ILogger<Worker> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient("SCREAM");
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        _hostName = GetConfigValue("MYSQL_BACKUP_HOSTNAME", "MySqlBackup:HostName");
        _userName = GetConfigValue("MYSQL_BACKUP_USERNAME", "MySqlBackup:UserName");
        _password = GetConfigValue("MYSQL_BACKUP_PASSWORD", "MySqlBackup:Password");
        _encryptionKey = GetConfigValue("MYSQL_BACKUP_ENCRYPTION_KEY", "MySqlBackup:EncryptionKey");
        _maxPacketSize = GetConfigValue("MYSQL_BACKUP_MAX_PACKET_SIZE", "MySqlBackup:MaxPacketSize", "1073741824");
        _threads = int.Parse(GetConfigValue("MYSQL_BACKUP_THREADS", "MySqlBackup:Threads",
            Environment.ProcessorCount.ToString()));

        var backupFolder = GetConfigValue("MYSQL_BACKUP_FOLDER", "MySqlBackup:BackupFolder");
        _backupFolder = string.IsNullOrEmpty(backupFolder)
            ? DateTimeOffset.Now.ToString("yyyy-MM-dd-hh-mm")
            : backupFolder + "_" + DateTimeOffset.Now.ToString("yyyy-MM-dd-hh-mm");
    }

    private string GetConfigValue(string envKey, string configKey, string defaultValue = "")
    {
        var value = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        return _configuration[configKey] ?? defaultValue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
        {
            var currentBackupFolder = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd-HH-mm");
            var sw = Stopwatch.StartNew();

            // Generate backup jobs based on backup plans
            await GenerateBackupJobs(stoppingToken);

            // Process pending backup jobs
            await ProcessBackupJob(stoppingToken);

            sw.Stop();
            _logger.LogInformation($"DONE! - {sw.Elapsed.TotalMinutes} minutes");
        }
    }

    private async Task GenerateBackupJobs(CancellationToken stoppingToken)
    {
        try
        {
            var eligiblePlans = await _httpClient.GetFromJsonAsync<List<BackupPlan>>("plans/backup?isActive=true", stoppingToken) ?? new List<BackupPlan>();

            foreach (var plan in eligiblePlans)
            {
                if (plan.ScheduleType == ScheduleType.Triggered) continue;

                var existingJobs = await _httpClient.GetFromJsonAsync<List<BackupJob>>($"jobs/backup?planId={plan.Id}", stoppingToken) ?? new List<BackupJob>();

                var lastJob = existingJobs.OrderByDescending(j => j.CreatedAt).FirstOrDefault();

                if ((lastJob != null && (lastJob.Status == TaskStatus.Faulted || lastJob.Status == TaskStatus.Canceled)) || existingJobs.Any(j => j.Status < TaskStatus.RanToCompletion))
                {
                    string reason = lastJob != null && (lastJob.Status == TaskStatus.Faulted || lastJob.Status == TaskStatus.Canceled)
                        ? $"last job {lastJob.Id} is in {lastJob.Status} state"
                        : "active job exists";

                    _logger.LogInformation("Skipping plan {PlanId} - {Reason}", plan.Id, reason);
                    continue;
                }

                bool shouldCreate = false;

                switch (plan.ScheduleType)
                {
                    case ScheduleType.OneTime:
                        shouldCreate = true;
                        break;

                    case ScheduleType.Repeating:
                        if (string.IsNullOrEmpty(plan.ScheduleCron))
                        {
                            _logger.LogWarning("Repeating plan {PlanId} has no cron expression", plan.Id);
                            continue;
                        }

                        try
                        {
                            var cron = CronExpression.Parse(plan.ScheduleCron);
                            var nextRun = plan.NextRun ?? cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);

                            if (DateTime.UtcNow >= nextRun)
                            {
                                shouldCreate = true;
                                plan.NextRun = cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
                            }
                            else if (plan.NextRun == null)
                            {
                                plan.NextRun = nextRun;
                                await UpdateBackupPlan(plan, stoppingToken);
                            }
                        }
                        catch (CronFormatException ex)
                        {
                            _logger.LogError(ex, "Invalid cron format for plan {PlanId}", plan.Id);
                            continue;
                        }
                        break;
                }

                if (!shouldCreate) continue;

                if (await CreateBackupJob(plan, stoppingToken))
                {
                    plan.LastRun = DateTime.UtcNow;
                    await UpdateBackupPlan(plan, stoppingToken);
                    _logger.LogInformation("Successfully created backup job for plan {PlanId}", plan.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating backup jobs");
        }
    }

    private async Task<bool> CreateBackupJob(BackupPlan plan, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"plans/backup/{plan.Id}/run", new { }, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully created backup job for plan {PlanId}", plan.Id);
                return true;
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Failed to create backup job for plan {PlanId}. Status: {StatusCode}, Content: {Content}",
                    plan.Id, response.StatusCode, responseContent);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while creating backup job for plan {PlanId}", plan.Id);
            return false;
        }
    }

    private async Task<bool> UpdateBackupPlan(BackupPlan plan, CancellationToken ct)
    {
        try
        {
            var updateResponse = await _httpClient.PostAsJsonAsync("plans/backup", plan, ct);

            if (updateResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Updated backup plan {PlanId}", plan.Id);
                return true;
            }
            else
            {
                var error = await updateResponse.Content.ReadAsStringAsync(ct);
                _logger.LogError("Error updating backup plan {PlanId}: {Error}", plan.Id, error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception updating backup plan {PlanId}: {Error}", plan.Id, ex.Message);
            return false;
        }
    }

    private async Task ProcessBackupJob(CancellationToken stoppingToken)
    {
        try
        {
            // Fetch all jobs
            var allJobs = await _httpClient
                .GetFromJsonAsync<List<BackupJob>>("jobs/backup", stoppingToken);
            if (allJobs == null) return;

            // Only consider jobs in the Created state
            var pendingJobs = allJobs
                .Where(job => job.Status == TaskStatus.Created)
                .ToList();

            if (!pendingJobs.Any()) return;

            foreach (var job in pendingJobs)
            {
                // Retrieve the backup plan for the job.
                var backupPlan = await GetBackupPlan(job.BackupPlanId, stoppingToken);
                if (backupPlan == null)
                {
                    _logger.LogError("Backup plan not found for job {JobId}", job.Id);
                    continue;
                }

                // Retrieve the storage target.
                var storageTarget = await GetStorageTargetWithRetry(backupPlan.StorageTargetId, stoppingToken);
                if (storageTarget == null)
                {
                    _logger.LogError(
                        "Storage target not found for backup plan {BackupPlanId} in job {JobId}",
                        backupPlan.Id, job.Id);
                    continue;
                }

                // Mark the job as Running
                job.Status = TaskStatus.Running;
                job.StartedAt = DateTime.UtcNow;
                await UpdateBackupJob(job, stoppingToken);

                var sw = Stopwatch.StartNew();
                try
                {
                    // Gather selected items
                    var backupItems = await GetBackupItems(job.BackupPlanId, stoppingToken);
                    var selectedItems = backupItems?.Where(i => i.IsSelected).ToList();

                    if (selectedItems == null || !selectedItems.Any())
                    {
                        _logger.LogWarning("No selected items to backup for job {JobId}", job.Id);
                        await CompleteJob(job, stoppingToken);
                        continue;
                    }

                    // Process all items
                    await ProcessBackupItems(job, selectedItems, storageTarget, stoppingToken);

                    // After processing, check if any item failed
                    var statuses = await GetBackupJobStatuses(job.Id, stoppingToken);
                    var hasFailures = statuses?.Any(s => s.Status == TaskStatus.Faulted) ?? false;

                    if (hasFailures)
                    {
                        await FailJob(job, new Exception("Some items failed"), stoppingToken);
                    }
                    else
                    {
                        await CompleteJob(job, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed processing job {JobId}", job.Id);
                    await FailJob(job, ex, stoppingToken);
                }
                finally
                {
                    sw.Stop();
                    _logger.LogInformation("Processed job {JobId} in {Elapsed}", job.Id, sw.Elapsed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving or processing backup jobs");
        }
    }


    private async Task<BackupPlan> GetBackupPlan(long planId, CancellationToken token)
    {
        try
        {
            var plan = await _httpClient.GetFromJsonAsync<BackupPlan>($"plans/backup/{planId}", token);
            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get backup plan {PlanId}",
                planId);
        }

        return null;
    }

    private async Task<StorageTarget> GetStorageTargetWithRetry(long targetId, CancellationToken token)
    {
        try
        {
            var target = await _httpClient.GetFromJsonAsync<StorageTarget>($"targets/storage/{targetId}", token);
            return target;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get storage target {TargetId}",
                targetId);

        }

        return null;
    }

    private async Task ProcessBackupItems(
        BackupJob job,
        List<BackupItem> backupItems,
        StorageTarget storageTarget,
        CancellationToken stoppingToken)
    {
        bool hasFailures = false;

        // Process schema items first (triggers, events, functions)
        var schemas = backupItems
            .Where(i => i.DatabaseItem.Type is DatabaseItemType.Event
                or DatabaseItemType.FunctionProcedure
                or DatabaseItemType.Trigger)
            .Select(i => i.DatabaseItem.Schema)
            .Distinct();

        await Parallel.ForEachAsync(schemas, new ParallelOptions
        {
            MaxDegreeOfParallelism = _threads,
            CancellationToken = stoppingToken
        }, async (schema, ct) =>
        {
            var schemaItems = backupItems.Where(i =>
                i.DatabaseItem.Schema == schema &&
                i.DatabaseItem.Type is DatabaseItemType.Event
                    or DatabaseItemType.FunctionProcedure
                    or DatabaseItemType.Trigger).ToList();

            if (await ProcessSchemaItemsWithRetry(job, schema, schemaItems, storageTarget, ct) == false)
            {
                hasFailures = true;
            }
        });

        // Process tables/views
        await Parallel.ForEachAsync(
            backupItems.Where(i => i.DatabaseItem.Type is DatabaseItemType.TableStructure
                or DatabaseItemType.View),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _threads,
                CancellationToken = stoppingToken
            },
            async (item, ct) =>
            {
                if (await ProcessTableOrViewWithRetry(job, item, storageTarget, ct) == false)
                {
                    hasFailures = true;
                }
            }
        );

        // Process table data
        await Parallel.ForEachAsync(
            backupItems.Where(i => i.DatabaseItem.Type == DatabaseItemType.TableData),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _threads,
                CancellationToken = stoppingToken
            },
            async (item, ct) =>
            {
                if (await ProcessTableDataWithRetry(job, item, storageTarget, ct) == false)
                {
                    hasFailures = true;
                }
            }
        );

        // If any failures occurred, mark the job as failed
        if (hasFailures)
        {
            await FailJob(job, new Exception("Some items failed after retries"), stoppingToken);
        }
    }

    private async Task<bool> ProcessSchemaItemsWithRetry(
    BackupJob job,
    string schema,
    List<BackupItem> schemaItems,
    StorageTarget storageTarget,
    CancellationToken token)
    {
        bool allSucceeded = true;

        // Helper to run one dump command with retries, updating status only for the provided items.
        async Task<bool> RunWithRetriesAndMarkAsync(
            List<BackupItem> items,
            Func<Command> buildDumpCommand,
            string outputFileName)
        {
            var representative = items.First();
            // Mark the representative item as Running
            await UpdateItemStatus(job.Id, representative.Id, TaskStatus.Running, null, token);

            int retryCount = 0;
            while (retryCount < _maxRetries)
            {
                try
                {
                    // Execute the dump + compress/encrypt
                    var cmd = buildDumpCommand();
                    await CompressEncryptUpload(cmd, outputFileName, storageTarget, token, job, representative);

                    // On first success, mark all items in this group as completed
                    foreach (var itm in items)
                    {
                        await UpdateItemStatus(job.Id, itm.Id, TaskStatus.RanToCompletion, null, token);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount >= _maxRetries)
                    {
                        _logger.LogError(ex, "Max retries reached for {Schema} - {File}", schema, outputFileName);
                        // Only mark this group as failed
                        foreach (var itm in items)
                        {
                            await UpdateItemStatus(
                                job.Id,
                                itm.Id,
                                TaskStatus.Faulted,
                                $"Failed after {_maxRetries} retries: {ex.Message}",
                                token);
                        }
                        return false;
                    }
                    else
                    {
                        _logger.LogWarning(
                            ex,
                            "Retry {Retry}/{Max} for {Schema} - {File} after error: {Err}",
                            retryCount, _maxRetries, schema, outputFileName, ex.Message);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), token);
                    }
                }
            }

            // Should never get here
            return false;
        }

        // 1) Triggers
        var triggerItems = schemaItems
            .Where(i => i.DatabaseItem.Type == DatabaseItemType.Trigger)
            .ToList();
        if (triggerItems.Any())
        {
            allSucceeded &= await RunWithRetriesAndMarkAsync(
                triggerItems,
                () => Cli.Wrap("/usr/bin/mysqldump")
                          .WithArguments(a => a
                              .Add($"--host={_hostName}")
                              .Add($"--user={_userName}")
                              .Add($"--password={_password}")
                              .Add("--add-drop-trigger")
                              .Add("--dump-date")
                              .Add("--single-transaction")
                              .Add("--skip-add-locks")
                              .Add("--quote-names")
                              .Add("--no-data")
                              .Add("--no-create-db")
                              .Add("--no-create-info")
                              .Add("--skip-routines")
                              .Add("--skip-events")
                              .Add("--triggers")
                              .Add($"--max-allowed-packet={_maxPacketSize}")
                              .Add("--column-statistics=0")
                              .Add(schema)),
                $"{schema}-triggers.sql.xz.enc");
        }

        // 2) Events
        var eventItems = schemaItems
            .Where(i => i.DatabaseItem.Type == DatabaseItemType.Event)
            .ToList();
        if (eventItems.Any())
        {
            allSucceeded &= await RunWithRetriesAndMarkAsync(
                eventItems,
                () => Cli.Wrap("/usr/bin/mysqldump")
                          .WithArguments(a => a
                              .Add($"--host={_hostName}")
                              .Add($"--user={_userName}")
                              .Add($"--password={_password}")
                              .Add("--no-data")
                              .Add("--no-create-db")
                              .Add("--no-create-info")
                              .Add("--skip-routines")
                              .Add("--events")
                              .Add("--skip-triggers")
                              .Add("--dump-date")
                              .Add("--single-transaction")
                              .Add("--skip-add-locks")
                              .Add("--quote-names")
                              .Add($"--max-allowed-packet={_maxPacketSize}")
                              .Add("--column-statistics=0")
                              .Add(schema)),
                $"{schema}-events.sql.xz.enc");
        }

        // 3) Functions / Stored Procedures
        var funcItems = schemaItems
            .Where(i => i.DatabaseItem.Type == DatabaseItemType.FunctionProcedure)
            .ToList();
        if (funcItems.Any())
        {
            allSucceeded &= await RunWithRetriesAndMarkAsync(
                funcItems,
                () => Cli.Wrap("/usr/bin/mysqldump")
                          .WithArguments(a => a
                              .Add($"--host={_hostName}")
                              .Add($"--user={_userName}")
                              .Add($"--password={_password}")
                              .Add("--no-data")
                              .Add("--no-create-db")
                              .Add("--no-create-info")
                              .Add("--routines")
                              .Add("--skip-events")
                              .Add("--skip-triggers")
                              .Add("--single-transaction")
                              .Add("--skip-add-locks")
                              .Add("--quote-names")
                              .Add($"--max-allowed-packet={_maxPacketSize}")
                              .Add("--column-statistics=0")
                              .Add(schema)),
                $"{schema}-funcs.sql.xz.enc");
        }

        return allSucceeded;
    }

    private async Task<bool> ProcessTableOrViewWithRetry(
     BackupJob job,
     BackupItem item,
     StorageTarget storageTarget,
     CancellationToken token)
    {
        await UpdateItemStatus(job.Id, item.Id, TaskStatus.Running, null, token);

        int retryCount = 0;
        while (retryCount < _maxRetries)
        {
            try
            {
                switch (item.DatabaseItem.Type)
                {
                    case DatabaseItemType.TableStructure:
                        await DumpSchema(item.DatabaseItem.Schema, item.DatabaseItem.Name, storageTarget, token, job, item);
                        break;
                    case DatabaseItemType.View:
                        await DumpView(item.DatabaseItem.Schema, item.DatabaseItem.Name, storageTarget, token, job, item);
                        break;
                }

                await UpdateItemStatus(job.Id, item.Id, TaskStatus.RanToCompletion, null, token);
                return true;
            }
            catch (Exception ex)
            {
                retryCount++;

                if (retryCount == _maxRetries)
                {
                    _logger.LogError(ex, "Max retries reached. Failed to backup {Type} {Schema}.{Name}",
                        item.DatabaseItem.Type, item.DatabaseItem.Schema, item.DatabaseItem.Name);

                    await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted,
                        $"Failed after {_maxRetries} retries: {ex.Message}", token);

                    return false;
                }

                _logger.LogWarning(ex, "Failed to backup {Type} {Schema}.{Name}. Retry {RetryCount}/{MaxRetries}",
                    item.DatabaseItem.Type, item.DatabaseItem.Schema, item.DatabaseItem.Name, retryCount, _maxRetries);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), token); // Exponential backoff
            }
        }

        return false;
    }

    private async Task<bool> ProcessTableDataWithRetry(
    BackupJob job,
    BackupItem item,
    StorageTarget storageTarget,
    CancellationToken token)
    {
        await UpdateItemStatus(job.Id, item.Id, TaskStatus.Running, null, token);

        int retryCount = 0;
        while (retryCount < _maxRetries)
        {
            try
            {
                await DumpData(item.DatabaseItem.Schema, item.DatabaseItem.Name, storageTarget, token, job, item);
                await UpdateItemStatus(job.Id, item.Id, TaskStatus.RanToCompletion, null, token);
                return true;
            }
            catch (Exception ex)
            {
                retryCount++;

                if (retryCount == _maxRetries)
                {
                    _logger.LogError(ex, "Max retries reached. Failed to backup data for {Schema}.{Table}",
                        item.DatabaseItem.Schema, item.DatabaseItem.Name);

                    await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted,
                        $"Failed after {_maxRetries} retries: {ex.Message}", token);

                    return false;
                }

                _logger.LogWarning(ex, "Failed to backup data for {Schema}.{Table}. Retry {RetryCount}/{MaxRetries}",
                    item.DatabaseItem.Schema, item.DatabaseItem.Name, retryCount, _maxRetries);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), token); // Exponential backoff
            }
        }

        return false;
    }


    private async Task UpdateItemStatus(long jobId, long itemId, TaskStatus status,
     string? errorMessage, CancellationToken token)
    {

        try
        {
            // Get all statuses for this job
            var allStatuses = await _httpClient
                .GetFromJsonAsync<List<BackupItemStatus>>(
                    $"jobs/backup/items/status/{jobId}",
                    token);

            if (allStatuses == null)
            {
                throw new Exception($"Failed to retrieve statuses for job {jobId}");
            }

            var existingStatus = allStatuses.FirstOrDefault(s => s.BackupItemId == itemId);

            // Create or update status
            var statusUpdate = new BackupItemStatus
            {
                Id = existingStatus?.Id ?? 0,
                BackupJobId = jobId,
                BackupItemId = itemId,
                Status = status,
                ErrorMessage = errorMessage,
                StartedAt = status == TaskStatus.Running ? DateTime.UtcNow : existingStatus?.StartedAt,
                CompletedAt = status >= TaskStatus.RanToCompletion ? DateTime.UtcNow : null
            };

            // Use POST for both create and update as defined in the API
            var response = await _httpClient.PostAsJsonAsync(
                $"jobs/backup/items/status/{jobId}",
                statusUpdate,
                token);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(token);
                throw new Exception($"Failed to update status: {error}");
            }

            return; // Success, exit the retry loop
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update status for item {ItemId}.",
                itemId);

        }
    }

    private async Task<List<BackupItemStatus>?> GetBackupJobStatuses(long jobId, CancellationToken token)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<BackupItemStatus>>(
                $"jobs/backup/items/status/{jobId}",
                token
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get backup job statuses for job {JobId}",
                jobId);
        }

        return null;
    }


    private async Task<List<BackupItem>?> GetBackupItems(long planId, CancellationToken token)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<BackupItem>>(
                $"plans/backup/items/{planId}",
                token
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get backup items for plan {PlanId}.",
                planId);


        }

        return null;
    }

    private async Task UpdateBackupJob(BackupJob job, CancellationToken token)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"jobs/backup/{job.Id}", job, token);
            response.EnsureSuccessStatusCode();
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update backup job {JobId}. Retry {RetryCount}/{MaxRetries}",
                job.Id);
        }
    }

    private async Task CompleteJob(BackupJob job, CancellationToken token)
    {
        try
        {
            var statuses = await GetBackupJobStatuses(job.Id, token);
            if (statuses?.All(s => s.Status == TaskStatus.RanToCompletion) ?? false)
            {
                job.Status = TaskStatus.RanToCompletion;
                job.CompletedAt = DateTime.UtcNow;
                await UpdateBackupJob(job, token);
                _logger.LogInformation("Completed backup job {JobId}", job.Id);
            }
            else
            {
                await FailJob(job, new Exception("Some items failed"), token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing job {JobId}", job.Id);
            await FailJob(job, ex, token);
        }
    }

    private async Task FailJob(BackupJob job, Exception ex, CancellationToken token)
    {
        job.Status = TaskStatus.Faulted;
        job.CompletedAt = DateTime.UtcNow;
        await UpdateBackupJob(job, token);
        _logger.LogError(ex, "Failed processing backup job {JobId}", job.Id);
    }

    private async Task DumpData(string schema, string table, StorageTarget storageTarget,
      CancellationToken stoppingToken, BackupJob job, BackupItem item)
    {
        var time = Stopwatch.StartNew();
        try
        {
            // Data dump for table data.
            var dataDump = Cli.Wrap("/usr/bin/mysqldump")
                .WithArguments(args => args
                    .Add($"--host={_hostName}")
                    .Add($"--user={_userName}")
                    .Add($"--password={_password}")
                    .Add("--no-create-info")
                    .Add("--skip-triggers")
                    .Add("--skip-routines")
                    .Add("--skip-events")
                    .Add("--complete-insert")
                    .Add("--disable-keys")
                    .Add("--dump-date")
                    .Add("--extended-insert")
                    .Add("--no-autocommit")
                    .Add("--quick")
                    .Add("--single-transaction")
                    .Add("--skip-add-locks")
                    .Add("--quote-names")
                    .Add($"--max-allowed-packet={_maxPacketSize}")
                    .Add("--column-statistics=0")
                    .Add(schema)
                    .Add(table))
                .WithStandardErrorPipe(new LoggerPipeTarget(_logger));

            _logger.LogInformation($"-- Dumping {schema}.{table} - DATA");
            await CompressEncryptUpload(dataDump, $"{schema}.{table}-data.sql.xz.enc", storageTarget, stoppingToken, job, item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"ERROR - Dumping {schema}.{table} - DATA");
            // Update item status to failed
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted,
                $"Failed to dump data for {schema}.{table}: {ex.Message}", stoppingToken);
            throw;
        }
        finally
        {
            time.Stop();
            _logger.LogInformation($"-- Dumped {schema}.{table} - DATA -- Took {time.ElapsedMilliseconds}ms");
        }
    }

    private async Task DumpSchema(string schema, string table, StorageTarget storageTarget,
    CancellationToken stoppingToken, BackupJob job, BackupItem item)
    {
        var time = Stopwatch.StartNew();
        try
        {
            var schemaDump = Cli.Wrap("/usr/bin/mysqldump")
                .WithArguments(args => args
                    .Add($"--host={_hostName}")
                    .Add($"--user={_userName}")
                    .Add($"--password={_password}")
                    .Add("--add-drop-table")
                    .Add("--dump-date")
                    .Add("--single-transaction")
                    .Add("--skip-add-locks")
                    .Add("--quote-names")
                    .Add("--no-data")
                    .Add("--skip-routines")
                    .Add("--skip-events")
                    .Add("--skip-triggers")
                    .Add($"--max-allowed-packet={_maxPacketSize}")
                    .Add("--column-statistics=0")
                    .Add(schema)
                    .Add(table))
                .WithStandardErrorPipe(new LoggerPipeTarget(_logger));

            _logger.LogInformation($"-- Dumping {schema}.{table} - SCHEMA");
            await CompressEncryptUpload(schemaDump, $"{schema}.{table}-schema.sql.xz.enc", storageTarget, stoppingToken, job, item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"ERROR Dumping {schema}.{table} - SCHEMA");
            // Update item status to failed
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted,
                $"Failed to dump schema for {schema}.{table}: {ex.Message}", stoppingToken);
            throw;
        }
        finally
        {
            time.Stop();
            _logger.LogInformation($"-- Dumped {schema}.{table} - SCHEMA -- Took {time.ElapsedMilliseconds}ms");
        }
    }

    private async Task DumpView(string schema, string table, StorageTarget storageTarget,
    CancellationToken stoppingToken, BackupJob job, BackupItem item)
    {
        var time = Stopwatch.StartNew();
        try
        {
            // Dump for a view is similar to schema dump.
            var viewDump = Cli.Wrap("/usr/bin/mysqldump")
                .WithArguments(args => args
                    .Add($"--host={_hostName}")
                    .Add($"--user={_userName}")
                    .Add($"--password={_password}")
                    .Add("--add-drop-table")
                    .Add("--dump-date")
                    .Add("--single-transaction")
                    .Add("--skip-add-locks")
                    .Add("--quote-names")
                    .Add("--no-data")
                    .Add("--skip-routines")
                    .Add("--skip-events")
                    .Add("--skip-triggers")
                    .Add($"--max-allowed-packet={_maxPacketSize}")
                    .Add("--column-statistics=0")
                    .Add(schema)
                    .Add(table))
                .WithStandardErrorPipe(new LoggerPipeTarget(_logger));

            _logger.LogInformation($"-- Dumping {schema}.{table} - VIEW");
            await CompressEncryptUpload(viewDump, $"{schema}.{table}-view.sql.xz.enc", storageTarget, stoppingToken, job, item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"ERROR Dumping {schema}.{table} - VIEW");
            // Update item status to failed
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted,
                $"Failed to dump view for {schema}.{table}: {ex.Message}", stoppingToken);
            throw;
        }
        finally
        {
            time.Stop();
            _logger.LogInformation($"-- Dumped {schema}.{table} - VIEW -- Took {time.ElapsedMilliseconds}ms");
        }
    }

    private async Task CompressEncryptUpload(Command dumpCommand, string fileName, StorageTarget storageTarget,
      CancellationToken stoppingToken, BackupJob job, BackupItem item)
    {
        if (storageTarget is LocalStorageTarget localTarget)
        {
            await HandleLocalStorage(dumpCommand, fileName, localTarget, stoppingToken, job, item);
        }
        else if (storageTarget is S3StorageTarget)
        {
            // If needed, implement S3 storage handling here.
            throw new NotSupportedException("S3 storage is not implemented in this example.");
        }
        else
        {
            throw new NotSupportedException($"Storage type {storageTarget.GetType().Name} not supported");
        }
    }

    private async Task HandleLocalStorage(Command dumpCommand, string fileName, LocalStorageTarget storageTarget,
    CancellationToken stoppingToken, BackupJob job, BackupItem item)
    {
        var time = Stopwatch.StartNew();
        try
        {
            var fullPath = Path.Combine(
                storageTarget.Path,
                _hostName,
                _backupFolder,
                fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            int attempt = 0;
            while (attempt < _maxRetries)
            {
                try
                {
                    await (dumpCommand
                           | Cli.Wrap("xz").WithArguments($"-T {_threads} -3 -c")
                           | Cli.Wrap("openssl").WithArguments($"enc -aes-256-cbc -pbkdf2 -iter 20000 -k {_encryptionKey}")
                           | PipeTarget.ToFile(fullPath))
                        .ExecuteAsync(stoppingToken);

                    break;
                }
                catch (Exception ex)
                {
                    attempt++;
                    if (attempt >= _maxRetries)
                    {
                        _logger.LogError(ex, "All {MaxRetries} attempts failed for {FileName}", _maxRetries, fileName);

                        // Update item status to failed
                        if (item != null)
                        {
                            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted,
                                $"Failed to process {fileName}: {ex.Message}", stoppingToken);
                        }

                        throw;
                    }

                    _logger.LogWarning(ex, "Backup attempt {Attempt} of {MaxRetries} failed for {FileName}. Retrying in 2 seconds...",
                        attempt, _maxRetries, fileName);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }

            time.Stop();
            _logger.LogInformation("Local backup saved to {FilePath} in {ElapsedTime}ms", fullPath, time.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            time.Stop();
            _logger.LogError(ex, "Local storage backup failed for {FileName} after {ElapsedTime}ms",
                fileName, time.ElapsedMilliseconds);

            // Update item status to failed if not already updated
            if (item != null)
            {
                await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted,
                    $"Local storage backup failed: {ex.Message}", stoppingToken);
            }

            throw;
        }
    }
}