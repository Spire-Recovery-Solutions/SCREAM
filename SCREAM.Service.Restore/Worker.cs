using SCREAM.Data.Entities.Backup;
using SCREAM.Data.Entities.Restore;
using SCREAM.Data.Enums;
using System.Net.Http.Json;

namespace SCREAM.Service.Restore
{
    public class Worker(ILogger<Worker> logger, IHttpClientFactory httpClientFactory) : BackgroundService
    {
        private readonly ILogger<Worker> _logger = logger;
        private readonly HttpClient _httpClient = httpClientFactory.CreateClient("SCREAM");

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Restore Worker running at: {time}", DateTimeOffset.Now);

                // Process backup jobs that have completed but haven't triggered a restore.
                await ProcessCompletedBackupJobs(stoppingToken);

                // Generate restore jobs for active restore plans that are due to run.
                await GenerateRestoreJobs(stoppingToken);

                await Task.Delay(1000, stoppingToken);
            }
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
                    var restorePlans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>("plans/restore", stoppingToken);
                    if (restorePlans is null)
                        continue;

                    // Find restore plans that are triggered by this backup job's plan.
                    var matchingPlans = restorePlans.Where(r => r.SourceBackupPlanId == backupJob.BackupPlanId &&
                    r.ScheduleType == ScheduleType.Triggered)
                    .ToList();
                    foreach (var restorePlan in matchingPlans)
                    {
                        // Create a new restore job for this restore plan.
                        var response = await _httpClient.PostAsync(
                            $"jobs/restore/{restorePlan.Id}/run", null, stoppingToken);
                        if (response.IsSuccessStatusCode)
                        {
                            _logger.LogInformation("Created restore job for restore plan {RestorePlanId}", restorePlan.Id);
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

        private async Task GenerateRestoreJobs(CancellationToken stoppingToken)
        {
            try
            {
                // Retrieve restore plans from the API.
                var restorePlans = await _httpClient.GetFromJsonAsync<List<RestorePlan>>("plans/restore", stoppingToken);
                if (restorePlans is null)
                {
                    _logger.LogWarning("No restore plans received from API.");
                    return;
                }

                // Filter for active restore plans that are not triggered and have no NextRun.
                var eligiblePlans = restorePlans
                    .Where(r => r.IsActive && r.ScheduleType != ScheduleType.Triggered && r.NextRun == null)
                    .ToList();

                foreach (var restorePlan in eligiblePlans)
                {
                    // Check if there are active jobs for this restore plan.
                    // (Assuming the API returns a Jobs collection in the restore plan.)
                    if (restorePlan.Jobs != null &&
                        restorePlan.Jobs.Any(j => j.Status >= TaskStatus.Created && j.Status < TaskStatus.RanToCompletion))
                    {
                        continue;
                    }

                    // Calculate NextRun using the plan's logic.
                    var nextRun = restorePlan.GetNextRun(DateTime.UtcNow);
                    if (nextRun != null)
                    {
                        restorePlan.NextRun = nextRun;
                        // Update the restore plan via API using POST (which handles both creation and update).
                        var updateResponse = await _httpClient.PostAsJsonAsync("plans/restore", restorePlan, stoppingToken);
                        if (updateResponse.IsSuccessStatusCode)
                        {
                            _logger.LogInformation("Restore plan {RestorePlanId} updated with NextRun {NextRun}", restorePlan.Id, restorePlan.NextRun);
                        }
                        else
                        {
                            var error = await updateResponse.Content.ReadAsStringAsync(stoppingToken);
                            _logger.LogError("Error updating restore plan {RestorePlanId}: {Error}", restorePlan.Id, error);
                        }
                    }

                    // Create a new restore job for the plan.
                    try
                    {
                        var response = await _httpClient.PostAsJsonAsync($"jobs/restore/{restorePlan.Id}/run", new { }, stoppingToken);

                        // Log full response details
                        var responseContent = await response.Content.ReadAsStringAsync(stoppingToken);
                        _logger.LogInformation("Response Status: {StatusCode}", response.StatusCode);
                        _logger.LogInformation("Response Content: {Content}", responseContent);

                        if (response.IsSuccessStatusCode)
                        {
                            _logger.LogInformation("Created restore job for restore plan {RestorePlanId}", restorePlan.Id);
                        }
                        else
                        {
                            _logger.LogError("Failed to create restore job for plan {RestorePlanId}. Status: {StatusCode}, Content: {Content}",
                                restorePlan.Id, response.StatusCode, responseContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception occurred while creating restore job for plan {RestorePlanId}", restorePlan.Id);
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