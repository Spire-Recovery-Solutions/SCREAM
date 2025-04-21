using Amazon.S3;
using Amazon.S3.Model;
using CliWrap;
using Cronos;
using Microsoft.IO;
using SCREAM.Data.Entities;
using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Backup.BackupItems;
using SCREAM.Data.Entities.StorageTargets;
using SCREAM.Data.Enums;
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
    private readonly string _maxPacketSize;
    private readonly string _backupFolder;
    private readonly IConfiguration _configuration;
    private readonly int _threads;
    private readonly HttpClient _httpClient;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly AmazonS3Client _b2Client;
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

        var b2Config = new AmazonS3Config
        {
            ServiceURL = GetConfigValue("MYSQL_BACKUP_B2_SERVICE_URL", "MySqlBackup:BackblazeB2:ServiceURL"),
            ForcePathStyle = true
        };
        var options = new RecyclableMemoryStreamManager.Options()
        {
            BlockSize = 1024 * 1024,
            LargeBufferMultiple = 1024 * 1024,
            MaximumBufferSize = 500 * 1024 * 1024,
            MaximumLargePoolFreeBytes = 1024 * 1024 * 1024,
            MaximumSmallPoolFreeBytes = 100 * 1024 * 1024,
            AggressiveBufferReturn = true,

        };
        _memoryStreamManager = new RecyclableMemoryStreamManager(options);

        var b2AccessKey = GetConfigValue("MYSQL_BACKUP_B2_ACCESS_KEY", "MySqlBackup:BackblazeB2:AccessKey");
        var b2SecretKey = GetConfigValue("MYSQL_BACKUP_B2_SECRET_KEY", "MySqlBackup:BackblazeB2:SecretKey");
        var
        _b2Client = new AmazonS3Client(b2AccessKey, b2SecretKey, b2Config);

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
            // 1) Get all active backup plans
            var activePlans = await _httpClient
                .GetFromJsonAsync<List<BackupPlan>>("plans/backup?isActive=true", stoppingToken)
                ?? new List<BackupPlan>();

            foreach (var plan in activePlans)
            {
                // Skip triggered plans
                if (plan.ScheduleType == ScheduleType.Triggered)
                    continue;

                // 2) Load existing jobs for this plan
                var existingJobs = await _httpClient
                    .GetFromJsonAsync<List<BackupJob>>($"jobs/backup?planId={plan.Id}", stoppingToken)
                    ?? new List<BackupJob>();

                var lastJob = existingJobs
                    .OrderByDescending(j => j.CreatedAt)
                    .FirstOrDefault();

                bool jobsExist = existingJobs.Any();
                bool jobsStillRunning = existingJobs.Any(j => j.Status < TaskStatus.RanToCompletion);
                bool lastJobFailed = lastJob != null && lastJob.Status == TaskStatus.Faulted;

                // 3) Skip if something’s in progress or last run failed
                if (jobsStillRunning || lastJobFailed)
                {
                    var reason = lastJobFailed
                        ? $"last job {lastJob!.Id} failed"
                        : "a job is still running";
                    _logger.LogInformation("Skipping plan {PlanId} because {Reason}", plan.Id, reason);
                    continue;
                }

                // 4) Decide whether to create
                bool shouldCreateJob = false;

                switch (plan.ScheduleType)
                {
                    case ScheduleType.OneTime:
                        // Only once ever
                        shouldCreateJob = !jobsExist;
                        if (jobsExist)
                        {
                            _logger.LogInformation(
                                "Skipping OneTime plan {PlanId} because it already ran at {LastRun}",
                                plan.Id, lastJob!.CreatedAt);
                        }
                        break;

                    case ScheduleType.Repeating:
                        // Must have a cron
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
                                shouldCreateJob = true;
                                plan.NextRun = cron.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
                            }
                            else if (plan.NextRun == null)
                            {
                                // Initialize NextRun so we don’t keep hitting this branch
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

                // 5) Create the job if flagged
                if (shouldCreateJob)
                {
                    if (await CreateBackupJob(plan, stoppingToken))
                    {
                        plan.LastRun = DateTime.UtcNow;
                        await UpdateBackupPlan(plan, stoppingToken);
                        _logger.LogInformation("Successfully created backup job for plan {PlanId}", plan.Id);
                    }
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

            var pendingJobs = await _httpClient.GetFromJsonAsync<List<BackupJob>>("jobs/backup?statuses=Created&statuses=WaitingToRun", stoppingToken);

            if (pendingJobs == null || !pendingJobs.Any()) return;

            foreach (var job in pendingJobs)
            {
                job.Status = TaskStatus.Running;
                job.StartedAt = DateTime.UtcNow;
                await UpdateBackupJob(job, stoppingToken);

                var sw = Stopwatch.StartNew();
                try
                {
                    var backupPlan = await GetBackupPlan(job.BackupPlanId, stoppingToken);
                    if (backupPlan == null)
                    {
                        _logger.LogError("Backup plan not found for job {JobId}", job.Id);
                        await FailJob(job, new Exception("Backup plan not found"), stoppingToken);
                        continue;
                    }

                    var storageTarget = await GetStorageTarget(backupPlan.StorageTargetId, stoppingToken);
                    if (storageTarget == null)
                    {
                        _logger.LogError("Storage target not found for job {JobId}", job.Id);
                        await FailJob(job, new Exception("Storage target not found"), stoppingToken);
                        continue;
                    }

                    var backupItems = await GetBackupItems(job.BackupPlanId, stoppingToken);
                    var selectedItems = backupItems?.Where(i => i.IsSelected).ToList();
                    if (selectedItems == null || !selectedItems.Any())
                    {
                        _logger.LogWarning("No selected items for job {JobId}", job.Id);
                        await CompleteJob(job, stoppingToken);
                        continue;
                    }

                    bool allItemsSucceeded = await ProcessBackupItems(job, selectedItems, storageTarget, stoppingToken);
                    if (allItemsSucceeded)
                    {
                        await CompleteJob(job, stoppingToken);
                    }
                    else
                    {
                        await FailJob(job, new Exception("One or more items failed"), stoppingToken);
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
            _logger.LogError(ex, "Error processing backup jobs");
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

    private async Task<StorageTarget> GetStorageTarget(long targetId, CancellationToken token)
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

    private async Task<bool> ProcessBackupItems(
        BackupJob job,
        List<BackupItem> backupItems,
        StorageTarget storageTarget,
        CancellationToken stoppingToken)
    {
        int hasFailures = 0;

        // Get current statuses for items in the job with specific statuses
        var statuses = await _httpClient.GetFromJsonAsync<List<BackupItemStatus>>(
            $"jobs/backup/items/status/{job.Id}?statuses=Created&statuses=WaitingToRun",
            stoppingToken);

        // Filter eligible items based on returned statuses
        var eligibleItems = backupItems.Where(item =>
            statuses?.Any(s => s.BackupItemId == item.Id) ?? false).ToList();

        // FIRST: Process tables/views
        await Parallel.ForEachAsync(
            eligibleItems.Where(i => i.DatabaseItem.Type is DatabaseItemType.TableStructure
                or DatabaseItemType.View),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _threads,
                CancellationToken = stoppingToken
            },
            async (item, ct) =>
            {
                if (!await ProcessTableOrViewWithRetry(job, item, storageTarget, ct))
                {
                    Interlocked.Exchange(ref hasFailures, 1);
                }
            }
        );

        // SECOND: Process schema items (triggers, events, functions)
        var schemas = eligibleItems
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
            var schemaItems = eligibleItems.Where(i =>
                i.DatabaseItem.Schema == schema &&
                i.DatabaseItem.Type is DatabaseItemType.Event
                    or DatabaseItemType.FunctionProcedure
                    or DatabaseItemType.Trigger).ToList();
            if (!await ProcessSchemaItemsWithRetry(job, schema, schemaItems, storageTarget, ct))
            {
                Interlocked.Exchange(ref hasFailures, 1);
            }
        });

        // THIRD: Process table data
        await Parallel.ForEachAsync(
            eligibleItems.Where(i => i.DatabaseItem.Type == DatabaseItemType.TableData),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _threads,
                CancellationToken = stoppingToken
            },
            async (item, ct) =>
            {
                if (!await ProcessTableDataWithRetry(job, item, storageTarget, ct))
                {
                    Interlocked.Exchange(ref hasFailures, 1);
                }
            }
        );

        return hasFailures == 0;
    }

    private async Task<bool> ProcessSchemaItemsWithRetry(
     BackupJob job,
     string schema,
     List<BackupItem> schemaItems,
     StorageTarget storageTarget,
     CancellationToken token)
    {
        bool allSucceeded = true;

        // Get current statuses for all items in this job
        var statuses = await _httpClient.GetFromJsonAsync<List<BackupItemStatus>>(
            $"jobs/backup/items/status/{job.Id}", token) ?? [];

        // 1) Process Triggers
        var triggerItems = schemaItems
            .Where(i => i.DatabaseItem.Type == DatabaseItemType.Trigger)
            .ToList();

        if (triggerItems.Any())
        {
            // Check if any trigger items need processing
            var needsProcessing = false;
            foreach (var item in triggerItems)
            {
                var status = statuses.FirstOrDefault(s => s.BackupItemId == item.Id);
                if (status == null || (status.Status != TaskStatus.RanToCompletion && status.Status != TaskStatus.Faulted))
                {
                    needsProcessing = true;
                    break;
                }
            }

            if (needsProcessing)
            {
                // Pick one representative item to track the operation
                var representative = triggerItems.First();
                await UpdateItemStatus(job.Id, representative.Id, TaskStatus.Running, null, token);

                bool success = false;
                string errorMessage = null;

                // Try the operation with retries
                int retryCount = 0;
                while (retryCount < _maxRetries && !success)
                {
                    try
                    {
                        var triggersDump =
                 Cli.Wrap("/usr/bin/mysqldump")
                     .WithArguments(args => args
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
                         .Add("--skip-column-statistics")
                         .Add(schema)
                     )
                     .WithStandardErrorPipe(PipeTarget.ToDelegate(line => { if (!string.IsNullOrEmpty(line)) _logger.LogError(line); }));

                        await CompressEncryptUpload(triggersDump, $"{schema}-triggers.sql.xz.enc", storageTarget, token, job, representative);
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        if (retryCount >= _maxRetries)
                        {
                            errorMessage = $"Failed after {_maxRetries} retries: {ex.Message}";
                            _logger.LogError(ex, "Max retries reached for {Schema} - triggers", schema);
                        }
                        else
                        {
                            _logger.LogWarning(ex, "Retry {Retry}/{Max} for {Schema} - triggers after error: {Err}",
                                retryCount, _maxRetries, schema, ex.Message);
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), token);
                        }
                    }
                }

                // Update each item's status individually based on success/failure
                foreach (var item in triggerItems)
                {
                    var status = statuses.FirstOrDefault(s => s.BackupItemId == item.Id);
                    // Only update items that aren't already in a final state
                    if (status == null || (status.Status != TaskStatus.RanToCompletion && status.Status != TaskStatus.Faulted))
                    {
                        await UpdateItemStatus(
                            job.Id,
                            item.Id,
                            success ? TaskStatus.RanToCompletion : TaskStatus.Faulted,
                            success ? null : errorMessage,
                            token);
                    }
                }

                allSucceeded &= success;
            }
        }

        // 2) Process Events
        var eventItems = schemaItems
            .Where(i => i.DatabaseItem.Type == DatabaseItemType.Event)
            .ToList();

        if (eventItems.Any())
        {
            // Check if any event items need processing
            var needsProcessing = false;
            foreach (var item in eventItems)
            {
                var status = statuses.FirstOrDefault(s => s.BackupItemId == item.Id);
                if (status == null || (status.Status != TaskStatus.RanToCompletion && status.Status != TaskStatus.Faulted))
                {
                    needsProcessing = true;
                    break;
                }
            }

            if (needsProcessing)
            {
                // Pick one representative item to track the operation
                var representative = eventItems.First();
                await UpdateItemStatus(job.Id, representative.Id, TaskStatus.Running, null, token);

                bool success = false;
                string errorMessage = null;

                // Try the operation with retries
                int retryCount = 0;
                while (retryCount < _maxRetries && !success)
                {
                    try
                    {
                        var eventsDump = Cli.Wrap("/usr/bin/mysqldump")
                .WithArguments(args => args
                    .Add($"--host={_hostName}")
                    .Add($"--user={_userName}")
                    .Add($"--password={_password}")

                    .Add($"--no-data")
                    .Add($"--no-create-db")
                    .Add($"--no-create-info")
                    .Add($"--skip-routines")
                    .Add($"--events")
                    .Add($"--skip-triggers")

                    .Add($"--dump-date")
                    .Add($"--single-transaction")
                    .Add($"--skip-add-locks")
                    .Add($"--quote-names")



                    .Add($"--max-allowed-packet={_maxPacketSize}")
                    .Add("--skip-column-statistics")
                    .Add(schema))
                    .WithStandardErrorPipe(PipeTarget.ToDelegate(line => { if (!string.IsNullOrEmpty(line)) _logger.LogError(line); }));

                        await CompressEncryptUpload(eventsDump, $"{schema}-events.sql.xz.enc", storageTarget, token, job, representative);
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        if (retryCount >= _maxRetries)
                        {
                            errorMessage = $"Failed after {_maxRetries} retries: {ex.Message}";
                            _logger.LogError(ex, "Max retries reached for {Schema} - events", schema);
                        }
                        else
                        {
                            _logger.LogWarning(ex, "Retry {Retry}/{Max} for {Schema} - events after error: {Err}",
                                retryCount, _maxRetries, schema, ex.Message);
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), token);
                        }
                    }
                }

                // Update each item's status individually based on success/failure
                foreach (var item in eventItems)
                {
                    var status = statuses.FirstOrDefault(s => s.BackupItemId == item.Id);
                    // Only update items that aren't already in a final state
                    if (status == null || (status.Status != TaskStatus.RanToCompletion && status.Status != TaskStatus.Faulted))
                    {
                        await UpdateItemStatus(
                            job.Id,
                            item.Id,
                            success ? TaskStatus.RanToCompletion : TaskStatus.Faulted,
                            success ? null : errorMessage,
                            token);
                    }
                }

                allSucceeded &= success;
            }
        }

        // 3) Process Functions / Stored Procedures
        var funcItems = schemaItems
            .Where(i => i.DatabaseItem.Type == DatabaseItemType.FunctionProcedure)
            .ToList();

        if (funcItems.Any())
        {
            // Check if any function/procedure items need processing
            var needsProcessing = false;
            foreach (var item in funcItems)
            {
                var status = statuses.FirstOrDefault(s => s.BackupItemId == item.Id);
                if (status == null || (status.Status != TaskStatus.RanToCompletion && status.Status != TaskStatus.Faulted))
                {
                    needsProcessing = true;
                    break;
                }
            }

            if (needsProcessing)
            {
                // Pick one representative item to track the operation
                var representative = funcItems.First();
                await UpdateItemStatus(job.Id, representative.Id, TaskStatus.Running, null, token);

                bool success = false;
                string errorMessage = null;

                // Try the operation with retries
                int retryCount = 0;
                while (retryCount < _maxRetries && !success)
                {
                    try
                    {
                        var funcDumps =
                Cli.Wrap("/usr/bin/mysqldump")
                    .WithArguments(args => args
                        .Add($"--host={_hostName}")
                        .Add($"--user={_userName}")
                        .Add($"--password={_password}")

                        .Add($"--no-data")
                        .Add($"--no-create-db")
                        .Add($"--no-create-info")
                        .Add($"--routines")
                        .Add($"--skip-events")
                        .Add($"--skip-triggers")

                        .Add($"--single-transaction")
                        .Add($"--skip-add-locks")
                        .Add($"--quote-names")

                        .Add($"--max-allowed-packet={_maxPacketSize}")
                        .Add("--skip-column-statistics")
                        .Add(schema)
                    )
                    .WithStandardErrorPipe(PipeTarget.ToDelegate(line => { if (!string.IsNullOrEmpty(line)) _logger.LogError(line); }));

                        await CompressEncryptUpload(funcDumps, $"{schema}-funcs.sql.xz.enc", storageTarget, token, job, representative);
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        if (retryCount >= _maxRetries)
                        {
                            errorMessage = $"Failed after {_maxRetries} retries: {ex.Message}";
                            _logger.LogError(ex, "Max retries reached for {Schema} - functions/procedures", schema);
                        }
                        else
                        {
                            _logger.LogWarning(ex, "Retry {Retry}/{Max} for {Schema} - functions/procedures after error: {Err}",
                                retryCount, _maxRetries, schema, ex.Message);
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), token);
                        }
                    }
                }

                // Update each item's status individually based on success/failure
                foreach (var item in funcItems)
                {
                    var status = statuses.FirstOrDefault(s => s.BackupItemId == item.Id);
                    // Only update items that aren't already in a final state
                    if (status == null || (status.Status != TaskStatus.RanToCompletion && status.Status != TaskStatus.Faulted))
                    {
                        await UpdateItemStatus(
                            job.Id,
                            item.Id,
                            success ? TaskStatus.RanToCompletion : TaskStatus.Faulted,
                            success ? null : errorMessage,
                            token);
                    }
                }

                allSucceeded &= success;
            }
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
                        await DumpStructure(item.DatabaseItem.Schema, item.DatabaseItem.Name, storageTarget, token, job, item);
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

        int maxRetries = 3;
        int attempt = 0;

        while (attempt < maxRetries)
        {
            try
            {
                var existingStatus = await _httpClient.GetFromJsonAsync<BackupItemStatus>(
                    $"jobs/backup/items/status/{jobId}/{itemId}", token);

                var statusUpdate = new BackupItemStatus
                {
                    Id = existingStatus?.Id ?? 0,
                    BackupJobId = jobId,
                    BackupItemId = itemId,
                    Status = status,
                    ErrorMessage = errorMessage,
                    StartedAt = status == TaskStatus.Running ? DateTime.UtcNow : null,
                    CompletedAt = status >= TaskStatus.RanToCompletion ? DateTime.UtcNow : null
                };

                var response = await _httpClient.PostAsJsonAsync(
                    $"jobs/backup/items/status/{jobId}",
                    statusUpdate,
                    token);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(token);
                    throw new Exception($"Failed to update status: {error}");
                }

                return; // Success
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt >= maxRetries)
                {
                    _logger.LogError(ex, "Failed to update status for item {ItemId} after {MaxRetries} attempts.", itemId, maxRetries);
                    throw;
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), token);
            }
        }
    }

    private async Task<List<BackupItem>?> GetBackupItems(long planId, CancellationToken token)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<BackupItem>>(
                $"plans/backup/items/{planId}", token);
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
        job.Status = TaskStatus.RanToCompletion;
        job.CompletedAt = DateTime.UtcNow;
        await UpdateBackupJob(job, token);
        _logger.LogInformation("Completed backup job {JobId}", job.Id);
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
                   .Add("--skip-column-statistics")
                   .Add(schema)
                   .Add(table)
               )
               .WithStandardErrorPipe(PipeTarget.ToDelegate(line => { if (!string.IsNullOrEmpty(line)) _logger.LogError(line); }));


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

    private async Task DumpStructure(string structure, string table, StorageTarget storageTarget,
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

                    .Add($"--add-drop-table")
                    .Add($"--dump-date")
                    .Add($"--single-transaction")
                    .Add($"--skip-add-locks")
                    .Add($"--quote-names")

                    .Add($"--no-data")
                    .Add($"--skip-routines")
                    .Add($"--skip-events")
                    .Add($"--skip-triggers")

                    .Add($"--max-allowed-packet={_maxPacketSize}")
                    .Add("--skip-column-statistics")
                    .Add(structure)
                    .Add(table)
                )
                .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
{
    if (!string.IsNullOrEmpty(line))
        _logger.LogError(line);
}));
            _logger.LogInformation($"-- Dumping {structure}.{table} - STRUCTURE");
            await CompressEncryptUpload(schemaDump, $"{structure}.{table}-structure.sql.xz.enc", storageTarget, stoppingToken, job, item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"ERROR Dumping {structure}.{table} - STRUCTURE");
            // Update item status to failed
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted,
                $"Failed to dump structure for {structure}.{table}: {ex.Message}", stoppingToken);
            throw;
        }
        finally
        {
            time.Stop();
            _logger.LogInformation($"-- Dumped {structure}.{table} - SCHEMA -- Took {time.ElapsedMilliseconds}ms");
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

                    .Add($"--add-drop-table")
                    .Add($"--dump-date")
                    .Add($"--single-transaction")
                    .Add($"--skip-add-locks")
                    .Add($"--quote-names")

                    .Add($"--no-data")
                    .Add($"--skip-routines")
                    .Add($"--skip-events")
                    .Add($"--skip-triggers")

                    .Add($"--max-allowed-packet={_maxPacketSize}")
                    .Add("--skip-column-statistics")
                    .Add(schema)
                    .Add(table)
                )
                .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
{
    if (!string.IsNullOrEmpty(line))
        _logger.LogError(line);
}));

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
        else if (storageTarget is S3StorageTarget s3Target)
        {
            await HandleS3Storage(dumpCommand, fileName, s3Target, stoppingToken, job, item);
        }
        else
        {
            throw new NotSupportedException($"Storage type {storageTarget.GetType().Name} not supported");
        }
    }

    private async Task HandleS3Storage(
    Command dumpCommand,
    string fileName,
    S3StorageTarget storageTarget,
    CancellationToken stoppingToken,
    BackupJob job,
    BackupItem item)
    {
        var time = Stopwatch.StartNew();
        string uploadId = null;
        List<PartETag> partETags = null;

        try
        {
            var fileKey = Path.Combine(_hostName, _backupFolder, fileName);
            partETags = new List<PartETag>();
            var partNumber = 1;

            const long minPartSize = 5L * 1024L * 1024L; // 5 MB
            const long maxPartSize = 500L * 1024L * 1024L; // 500 MB
            long totalBytesRead = 0;
            var isMultiPartUpload = false;

            var xzCommand = Cli.Wrap("xz")
                .WithArguments(builder => builder
                    .Add($"-T {_threads}")
                    .Add("-3")
                    .Add("-c"));

            var encryptCommand = Cli.Wrap("openssl")
                .WithArguments($"enc -aes-256-cbc -pbkdf2 -iter 20000 -k {_encryptionKey} -in - -out -");

            await (dumpCommand | xzCommand | encryptCommand)
                .WithStandardOutputPipe(PipeTarget.Create(async (stream, token) =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            await using var remoteStream = _memoryStreamManager.GetStream(
                                $"BackupUpload.{fileName}.Part{partNumber}");

                            var buffer = new byte[81920];
                            var reachedEnd = false;

                            while (remoteStream.Length < maxPartSize && !reachedEnd)
                            {
                                var bytesRead = await stream.ReadAsync(buffer, token);
                                if (bytesRead == 0)
                                {
                                    reachedEnd = true;
                                    break;
                                }

                                totalBytesRead += bytesRead;
                                await remoteStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);

                                if (totalBytesRead >= minPartSize && !isMultiPartUpload)
                                {
                                    isMultiPartUpload = true;
                                    uploadId = await StartMultipartUpload(storageTarget.BucketName, fileKey, stoppingToken);
                                    _logger.LogInformation(
                                        $"{fileName} - Multi-Part Upload Started - UploadId: {uploadId}");
                                }
                            }

                            if (remoteStream.Length > 0)
                            {
                                remoteStream.Position = 0;

                                if (isMultiPartUpload)
                                {
                                    await UploadPart(storageTarget.BucketName, fileKey, uploadId, partNumber,
                                        remoteStream, partETags, stoppingToken);
                                    partNumber++;
                                }
                                else
                                {
                                    await PerformSinglePartUpload(storageTarget.BucketName, fileKey,
                                        remoteStream, fileName, totalBytesRead, stoppingToken);
                                }
                            }

                            if (reachedEnd)
                            {
                                if (isMultiPartUpload)
                                {
                                    await CompleteMultipartUpload(storageTarget.BucketName, fileKey, uploadId,
                                        partETags, fileName, stoppingToken);
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"{fileName} - Upload pipeline error: {ex.Message}");
                        if (!string.IsNullOrEmpty(uploadId))
                        {
                            await AbortMultipartUpload(storageTarget.BucketName, fileKey, uploadId, stoppingToken);
                        }
                        throw;
                    }
                }))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(line => { if (!string.IsNullOrEmpty(line)) _logger.LogError(line); }))
                .ExecuteAsync(stoppingToken);

            time.Stop();
            _logger.LogInformation(
                $"{fileName} - S3 upload completed in {time.ElapsedMilliseconds}ms - Total: {totalBytesRead:N0} bytes");
        }
        catch (Exception ex)
        {
            time.Stop();
            _logger.LogError(ex, $"{fileName} - S3 upload failed after {time.ElapsedMilliseconds}ms");
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted,
                $"S3 upload failed: {ex.Message}", stoppingToken);
            throw;
        }
    }

    private async Task<string> StartMultipartUpload(string bucketName, string fileKey, CancellationToken ct)
    {
        var response = await _b2Client.InitiateMultipartUploadAsync(bucketName, fileKey, ct);
        return response.UploadId;
    }

    private async Task UploadPart(string bucketName, string fileKey, string uploadId, int partNumber,
        Stream stream, List<PartETag> partETags, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var request = new UploadPartRequest
            {
                BucketName = bucketName,
                Key = fileKey,
                UploadId = uploadId,
                PartNumber = partNumber,
                InputStream = stream,
                CalculateContentMD5Header = true
            };

            var response = await _b2Client.UploadPartAsync(request, ct);
            partETags.Add(new PartETag(partNumber, response.ETag));

            var mbps = (stream.Length / 1024.0 / 1024.0) / (timer.ElapsedMilliseconds / 1000.0);
            _logger.LogInformation(
                $"{fileKey} - Part {partNumber} uploaded ({stream.Length:N0} bytes) in {timer.ElapsedMilliseconds}ms ({mbps:N2} MB/s)");
        }
        finally
        {
            timer.Stop();
        }
    }

    private async Task CompleteMultipartUpload(string bucketName, string fileKey, string uploadId,
        List<PartETag> partETags, string fileName, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var request = new CompleteMultipartUploadRequest
            {
                BucketName = bucketName,
                Key = fileKey,
                UploadId = uploadId,
                PartETags = partETags
            };

            var response = await _b2Client.CompleteMultipartUploadAsync(request, ct);
            _logger.LogInformation(
                $"{fileName} - Multipart upload completed in {timer.ElapsedMilliseconds}ms ({partETags.Count} parts)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"{fileName} - Failed to complete multipart upload after {timer.ElapsedMilliseconds}ms");
            await AbortMultipartUpload(bucketName, fileKey, uploadId, ct);
            throw;
        }
        finally
        {
            timer.Stop();
        }
    }

    private async Task AbortMultipartUpload(string bucketName, string fileKey, string uploadId, CancellationToken ct)
    {
        try
        {
            await _b2Client.AbortMultipartUploadAsync(bucketName, fileKey, uploadId, ct);
            _logger.LogInformation($"{fileKey} - Successfully aborted multipart upload {uploadId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"{fileKey} - Failed to abort multipart upload {uploadId}");
        }
    }

    private async Task PerformSinglePartUpload(string bucketName, string fileKey,
        Stream stream, string fileName, long totalBytes, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = fileKey,
                InputStream = stream,
                CalculateContentMD5Header = true
            };

            var response = await _b2Client.PutObjectAsync(request, ct);

            var mbps = (totalBytes / 1024.0 / 1024.0) / (timer.ElapsedMilliseconds / 1000.0);
            _logger.LogInformation(
                $"{fileName} - Single-part upload completed ({totalBytes:N0} bytes) in {timer.ElapsedMilliseconds}ms ({mbps:N2} MB/s)");
        }
        finally
        {
            timer.Stop();
        }
    }

    private async Task HandleLocalStorage(
     Command dumpCommand,
     string fileName,
     LocalStorageTarget storageTarget,
     CancellationToken stoppingToken,
     BackupJob job,
     BackupItem item)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var backupPath = Path.Combine(
                Environment.GetEnvironmentVariable("LOCAL_STORAGE_ROOT") ?? "/backups",
                storageTarget.Path.Trim(Path.DirectorySeparatorChar),
                fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);

            var xzCmd = Cli.Wrap("xz")
                .WithArguments($"-T {_threads} -3 -c");

            var encryptCmd = Cli.Wrap("openssl")
                .WithArguments($"enc -aes-256-cbc -pbkdf2 -iter 20000 -k {_encryptionKey}");

            await (dumpCommand | xzCmd | encryptCmd)
                .WithStandardOutputPipe(PipeTarget.ToFile(backupPath))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
                    _logger.LogError("Backup error: {Error}", line)))
                .ExecuteAsync(stoppingToken);

            _logger.LogInformation("Backup saved to {Path}", backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed for {File}", fileName);
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted,
                $"Backup failed: {ex.Message}", stoppingToken);
            throw;
        }
        finally
        {
            sw.Stop();
            _logger.LogDebug("Backup operation took {Ms}ms", sw.ElapsedMilliseconds);
        }
    }
}