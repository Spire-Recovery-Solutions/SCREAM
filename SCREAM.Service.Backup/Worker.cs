using SCREAM.Data.Entities.Backup;
using System.Net.Http.Json;

namespace SCREAM.Service.Backup;

public class Worker(ILogger<Worker> logger, IHttpClientFactory httpClientFactory) : BackgroundService
{
    private readonly ILogger<Worker> _logger = logger;
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("SCREAM");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            // Use API calls instead of direct DbContext usage.
            await GenerateBackupJobs(stoppingToken);

            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task GenerateBackupJobs(CancellationToken stoppingToken)
    {
        try
        {
            // Retrieve only active backup plans with NextRun == null from the API.
            var backupPlans = await _httpClient.GetFromJsonAsync<List<BackupPlan>>(
                "plans/backup?isActive=true&nextRunIsNull=true", stoppingToken);
            if (backupPlans is null)
            {
                _logger.LogWarning("No backup plans found from API.");
                return;
            }


            foreach (var backupPlan in backupPlans)
            {
                // Retrieve active jobs for this backup plan via the API.
                var activeJobs = await _httpClient.GetFromJsonAsync<List<BackupJob>>(
                    $"jobs/backup?planId={backupPlan.Id}", stoppingToken);

                // Check if there are any jobs that are already created or running.
                if (activeJobs != null && activeJobs.Any(job => job.Status >= TaskStatus.Created
                                                                && job.Status < TaskStatus.RanToCompletion))
                {
                    continue;
                }
                //TODO: Check for retries on the Faulted jobs
                // var faultedJobs = backupPlan.Jobs.Where(w => w.Status == TaskStatus.Faulted).ToList();
                // if (faultedJobs.Any())
                // {
                //     foreach (var faultedJob in faultedJobs)
                //     {
                //         // Check if the job can be retried
                //         if (faultedJob.RetryCount < 3)
                //         {
                //             faultedJob.RetryCount++;
                //             dbContext.BackupJobs.Update(faultedJob);
                //         }
                //         else
                //         {
                //             // If the job has reached the max retry count, mark it as cancelled
                //             faultedJob.Status = TaskStatus.Canceled;
                //             dbContext.BackupJobs.Update(faultedJob);
                //         }
                //     }
                // }

                // Calculate NextRun
                var nextRun = backupPlan.GetNextRun(DateTime.UtcNow);
                if (nextRun != null)
                {
                    backupPlan.NextRun = nextRun;

                    // Update the backup plan via API (assumes a PUT endpoint exists).
                    var responseBack = await _httpClient.PostAsJsonAsync("plans/backup", backupPlan, stoppingToken);
                    if (responseBack.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Backup plan {BackupPlanId} updated with NextRun {NextRun}",
                            backupPlan.Id, backupPlan.NextRun);
                    }
                    else
                    {
                        var error = await responseBack.Content.ReadAsStringAsync(stoppingToken);
                        _logger.LogError("Error updating backup plan {BackupPlanId}: {Error}", backupPlan.Id, error);
                    }
                }

                // Create a new BackupJob by calling the API endpoint.
                var response = await _httpClient.PostAsync(
                    $"plans/backup/{backupPlan.Id}/run", null, stoppingToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Created backup job for plan {PlanId}", backupPlan.Id);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync(stoppingToken);
                    _logger.LogError("Failed to create backup job for plan {PlanId}: {Error}", backupPlan.Id, error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating backup jobs via API.");
        }
    }
}
