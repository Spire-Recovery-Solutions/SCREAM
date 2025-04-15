using CliWrap;
using Cronos;
using Microsoft.IO;
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
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;
    private readonly IConfiguration _configuration;
    private readonly int _threads;
    private readonly HttpClient _httpClient;

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

        var options = new RecyclableMemoryStreamManager.Options()
        {
            // Use 1MB blocks for better handling of large backup files
            BlockSize = 1024 * 1024,

            // Set large buffer multiple to 1MB for efficient chunking
            LargeBufferMultiple = 1024 * 1024,

            // Max single buffer size to 500MB (B2's recommended maximum part size)
            MaximumBufferSize = 500 * 1024 * 1024,

            // Cap total memory usage for pools
            MaximumLargePoolFreeBytes = 1024 * 1024 * 1024, // 1GB large pool
            MaximumSmallPoolFreeBytes = 100 * 1024 * 1024, // 100MB small pool

            // Return buffers aggressively to help manage memory
            AggressiveBufferReturn = true,

        };

        _memoryStreamManager = new RecyclableMemoryStreamManager(options);

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
            var allJobs = await _httpClient.GetFromJsonAsync<List<BackupJob>>("jobs/backup", stoppingToken);
            if (allJobs == null) return;

            var pendingJobs = allJobs.Where(job => job.Status == TaskStatus.Created).ToList();
            if (!pendingJobs.Any()) return;

            foreach (var job in pendingJobs)
            {
                var backupPlan = await _httpClient.GetFromJsonAsync<BackupPlan>(
                    $"plans/backup/{job.BackupPlanId}", stoppingToken);

                if (backupPlan?.StorageTarget == null)
                {
                    _logger.LogError("Missing storage target for job {JobId}", job.Id);
                    continue;
                }

                // Update job status to running
                job.Status = TaskStatus.Running;
                job.StartedAt = DateTime.UtcNow;
                await UpdateBackupJob(job, stoppingToken);

                var sw = Stopwatch.StartNew();
                try
                {
                    // Get backup items configuration and initialize statuses
                    var backupItems = await GetBackupItems(job.BackupPlanId, stoppingToken);
                    var selectedItems = backupItems?.Where(i => i.IsSelected).ToList();

                    if (selectedItems == null || !selectedItems.Any())
                    {
                        _logger.LogWarning("No selected items to backup for job {JobId}", job.Id);
                        await CompleteJob(job, stoppingToken);
                        continue;
                    }

                    // Process items
                    await ProcessBackupItems(job, selectedItems, backupPlan.StorageTarget, stoppingToken);

                    // Verify final statuses
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
                    _logger.LogError(ex, "Failed job {JobId}", job.Id);
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

    private async Task ProcessBackupItems(
        BackupJob job,
        List<BackupItem> backupItems,
        StorageTarget storageTarget,
        CancellationToken stoppingToken)
    {
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

            try
            {
                foreach (var item in schemaItems)
                {
                    await UpdateItemStatus(job.Id, item.Id, TaskStatus.Running, null, ct);
                }

                await ProcessSchemaItems(job, schema, backupItems, storageTarget, ct);

                foreach (var item in schemaItems)
                {
                    await UpdateItemStatus(job.Id, item.Id, TaskStatus.RanToCompletion, null, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed schema {Schema}", schema);
                foreach (var item in schemaItems)
                {
                    await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted, ex.Message, ct);
                }
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
            async (item, ct) => await ProcessTableOrView(job, item, storageTarget, ct)
        );

        // Process table data
        await Parallel.ForEachAsync(
            backupItems.Where(i => i.DatabaseItem.Type == DatabaseItemType.TableStructure),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _threads,
                CancellationToken = stoppingToken
            },
            async (item, ct) => await ProcessTableData(job, item, storageTarget, ct)
        );
    }

    private async Task ProcessTableOrView(BackupJob job, BackupItem item, StorageTarget storageTarget,
        CancellationToken token)
    {
        try
        {
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Running, null, token);

            switch (item.DatabaseItem.Type)
            {
                case DatabaseItemType.TableStructure:
                    await DumpSchema(item.DatabaseItem.Schema, item.DatabaseItem.Name, storageTarget, token);
                    break;
                case DatabaseItemType.View:
                    await DumpView(item.DatabaseItem.Schema, item.DatabaseItem.Name, storageTarget, token);
                    break;
            }

            await UpdateItemStatus(job.Id, item.Id, TaskStatus.RanToCompletion, null, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup {Type} {Schema}.{Name}",
                item.DatabaseItem.Type, item.DatabaseItem.Schema, item.DatabaseItem.Name);
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted, ex.Message, token);
        }
    }

    private async Task ProcessSchemaItems(BackupJob job, string schema, List<BackupItem> allItems,
        StorageTarget storageTarget,
        CancellationToken token)
    {
        List<BackupItem> schemaItems = new();

        try
        {
            schemaItems = allItems.Where(i => i.DatabaseItem.Schema == schema &&
                                              i.DatabaseItem.Type is DatabaseItemType.Event
                                                  or DatabaseItemType.FunctionProcedure
                                                  or DatabaseItemType.Trigger).ToList();

            if (!schemaItems.Any()) return;

            foreach (var item in schemaItems)
            {
                await UpdateItemStatus(job.Id, item.Id, TaskStatus.Running, null, token);
            }

            await DumpTriggers(schema, storageTarget, token);
            await DumpEvents(schema, storageTarget, token);
            await DumpFunctionsSps(schema, storageTarget, token);

            foreach (var item in schemaItems)
            {
                await UpdateItemStatus(job.Id, item.Id, TaskStatus.RanToCompletion, null, token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup schema items for {Schema}", schema);

            // Now schemaItems is in scope
            if (schemaItems.Any())
            {
                foreach (var item in schemaItems)
                {
                    await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted, ex.Message, token);
                }
            }
            else
            {
                _logger.LogWarning("No schema items found for failed schema {Schema}", schema);
            }
        }
    }

    private async Task ProcessTableData(BackupJob job, BackupItem item, StorageTarget storageTarget,
        CancellationToken token)
    {
        try
        {
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Running, null, token);
            await DumpData(item.DatabaseItem.Schema, item.DatabaseItem.Name, storageTarget, token);
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.RanToCompletion, null, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup data for {Schema}.{Table}",
                item.DatabaseItem.Schema, item.DatabaseItem.Name);
            await UpdateItemStatus(job.Id, item.Id, TaskStatus.Faulted, ex.Message, token);
        }
    }

    private async Task UpdateItemStatus(long jobId, long itemId, TaskStatus status,
        string? errorMessage, CancellationToken token)
    {
        try
        {
            // 1. Get existing status for this job+item
            var existingStatus = await _httpClient
                .GetFromJsonAsync<BackupItemStatus>(
                    $"jobs/backup/items/status/{jobId}/{itemId}",
                    token);

            if (existingStatus == null)
            {
                _logger.LogError("No status found for item {ItemId} in job {JobId}", itemId, jobId);
                return;
            }

            // 2. Update existing status
            var statusUpdate = new BackupItemStatus
            {
                Id = existingStatus.Id,
                BackupJobId = jobId,
                BackupItemId = itemId,
                Status = status,
                ErrorMessage = errorMessage,
                StartedAt = status == TaskStatus.Running ? DateTime.UtcNow : null,
                CompletedAt = status >= TaskStatus.RanToCompletion ? DateTime.UtcNow : null
            };

            // 3. Update using PUT
            var response = await _httpClient.PutAsJsonAsync(
                $"jobs/backup/items/status/{jobId}/{existingStatus.Id}",
                statusUpdate,
                token);


            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(token);
                _logger.LogWarning("Failed to update status for item {ItemId}: {Error}", itemId, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for item {ItemId}", itemId);
        }
    }

    private async Task<List<BackupItemStatus>?> GetBackupJobStatuses(long jobId, CancellationToken token)
    {
        return await _httpClient.GetFromJsonAsync<List<BackupItemStatus>>(
            $"jobs/backup/items/status/{jobId}",
            token
        );
    }


    private async Task<List<BackupItem>?> GetBackupItems(long planId, CancellationToken token)
    {
        return await _httpClient.GetFromJsonAsync<List<BackupItem>>(
            $"plans/backup/items/{planId}",
            token
        );
    }

    private async Task UpdateBackupJob(BackupJob job, CancellationToken token)
    {
        var response = await _httpClient.PutAsJsonAsync($"jobs/backup/{job.Id}", job, token);
        response.EnsureSuccessStatusCode();
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
        CancellationToken stoppingToken)
    {
        var time = Stopwatch.StartNew();
        try
        {
            //Data
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
                    .Add(table)
                )
                .WithStandardErrorPipe(new LoggerPipeTarget(_logger));

            _logger.LogInformation($"-- Dumping {schema}.{table} - DATA");
            await CompressEncryptUpload(dataDump, $"{schema}.{table}-data.sql.xz.enc", storageTarget, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"ERROR - Dumping {schema}.{table} - DATA - {ex.Message}");
        }

        time.Stop();
        _logger.LogInformation($"-- Dumped {schema}.{table} - DATA -- Took {time.ElapsedMilliseconds}ms");
    }

    private async Task DumpSchema(string schema, string table, StorageTarget storageTarget,
        CancellationToken stoppingToken)
    {
        var time = Stopwatch.StartNew();
        try
        {
            //Schema
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
                    .Add("--column-statistics=0")
                    .Add(schema)
                    .Add(table)
                )
                .WithStandardErrorPipe(new LoggerPipeTarget(_logger));

            //Compress Encrypt Upload
            _logger.LogInformation($"-- Dumping {schema}.{table} - SCHEMA");
            await CompressEncryptUpload(schemaDump,
                $"{schema}.{table}-schema.sql.xz.enc", storageTarget, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"ERROR Dumping {schema}.{table} SCHEMA - {ex.Message}");
        }

        time.Stop();
        _logger.LogInformation($"-- Dumped {schema}.{table} - SCHEMA -- Took {time.ElapsedMilliseconds}ms");
    }

    private async Task DumpView(string schema, string table, StorageTarget storageTarget,
        CancellationToken stoppingToken)
    {
        var time = Stopwatch.StartNew();
        try
        {
            //Schema
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
                    .Add("--column-statistics=0")
                    .Add(schema)
                    .Add(table)
                )
                .WithStandardErrorPipe(new LoggerPipeTarget(_logger));

            //Compress Encrypt Upload
            _logger.LogInformation($"-- Dumping {schema}.{table} - VIEW");
            await CompressEncryptUpload(schemaDump,
                $"{schema}.{table}-view.sql.xz.enc", storageTarget, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"ERROR Dumping {schema}.{table} VIEW - {ex.Message}");
        }

        time.Stop();
        _logger.LogInformation($"-- Dumped {schema}.{table} - VIEW -- Took {time.ElapsedMilliseconds}ms");
    }

    private async Task DumpEvents(string schema, StorageTarget storageTarget, CancellationToken stoppingToken)
    {
        var time = Stopwatch.StartNew();
        try
        {
            //Events
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
                    .Add("--column-statistics=0")
                    .Add(schema)
                )
                .WithStandardErrorPipe(new LoggerPipeTarget(_logger));
            //Compress Encrypt Upload
            _logger.LogInformation($"-- Dumping {schema} - EVENTS");
            await CompressEncryptUpload(eventsDump, $"{schema}-events.sql.xz.enc", storageTarget, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $@"ERROR $Dumping {schema} - EVENTS - {ex.Message}");
        }

        time.Stop();
        _logger.LogInformation($"-- Dumped {schema} - EVENTS -- Took {time.ElapsedMilliseconds}ms");
    }

    private async Task DumpFunctionsSps(string schema, StorageTarget storageTarget, CancellationToken stoppingToken)
    {
        var time = Stopwatch.StartNew();
        try
        {
            //Functions and SPs
            var triggersDump =
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
                        .Add("--column-statistics=0")
                        .Add(schema)
                    )
                    .WithStandardErrorPipe(new LoggerPipeTarget(_logger));

            //Compress Encrypt Upload
            _logger.LogInformation($"-- Dumping {schema} - Functions & SPs");
            await CompressEncryptUpload(triggersDump, $"{schema}-funcs.sql.xz.enc",
                storageTarget, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"ERROR Dumping {schema} - FUNCS & SPS - " + ex.Message);
        }

        time.Stop();
        _logger.LogInformation($"-- Dumped {schema} - FUNCS & SPS -- Took {time.ElapsedMilliseconds}ms");
    }

    private async Task DumpTriggers(string schema, StorageTarget storageTarget, CancellationToken stoppingToken)
    {
        var time = Stopwatch.StartNew();
        try
        {
            //Triggers
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
                        .Add("--column-statistics=0")
                        .Add(schema)
                    )
                    .WithStandardErrorPipe(new LoggerPipeTarget(_logger));

            //Compress Encrypt Upload
            _logger.LogInformation($"-- Dumping {schema} - TRIGGERS");
            await CompressEncryptUpload(triggersDump, $"{schema}-triggers.sql.xz.enc", storageTarget, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"ERROR Dumping {schema} - TRIGGERS - " + ex.Message);
        }

        time.Stop();
        _logger.LogInformation($"-- Dumped {schema} - TRIGGERS -- Took {time.ElapsedMilliseconds}ms");
    }


    private async Task CompressEncryptUpload(Command triggerDumpCommand, string fileName, StorageTarget storageTarget,
        CancellationToken stoppingToken)
    {
        if (storageTarget is LocalStorageTarget localTarget)
        {
            await HandleLocalStorage(triggerDumpCommand, fileName, localTarget, stoppingToken);
        }
        else if (storageTarget is S3StorageTarget)
        {
            //await HandleS3Storage(triggerDumpCommand, fileName, stoppingToken);
        }
        else
        {
            throw new NotSupportedException($"Storage type {storageTarget.GetType().Name} not supported");
        }
    }

    private async Task HandleLocalStorage(Command triggerDumpCommand, string fileName, LocalStorageTarget storageTarget,
        CancellationToken stoppingToken)
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

            await (triggerDumpCommand
                   | Cli.Wrap("xz").WithArguments($"-T {Environment.ProcessorCount} -3 -c")
                   | Cli.Wrap("openssl").WithArguments(
                       $"enc -aes-256-cbc -pbkdf2 -iter 20000 -k {_encryptionKey}")
                   | PipeTarget.ToFile(fullPath))
                .ExecuteAsync(stoppingToken);

            time.Stop();
            _logger.LogInformation(
                $"Local backup saved to {fullPath} in {time.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local storage backup failed for {FileName}", fileName);
            throw;
        }
    }
}