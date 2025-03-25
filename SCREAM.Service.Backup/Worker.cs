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
            await GenerateBackupJobs();

            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task GenerateBackupJobs()
    {
        try
        {
            // Get all backup plans via the API.
            var backupPlans = await _httpClient.GetFromJsonAsync<List<BackupPlan>>("plans/backup");
            if (backupPlans is null)
            {
                _logger.LogWarning("No backup plans found from API.");
                return;
            }

            // Filter for active backup plans with NextRun == null.
            foreach (var backupPlan in backupPlans.Where(bp => bp.IsActive && bp.NextRun == null))
            {
                // Check if there are any jobs that are already created or running.
                if (backupPlan.Jobs != null && backupPlan.Jobs.Any(job => job.Status >= TaskStatus.Created && job.Status < TaskStatus.RanToCompletion))
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
                    var responseback = await _httpClient.PostAsJsonAsync("plans/backup", backupPlan);
                    if (responseback.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Backup plan {BackupPlanId} updated with NextRun {NextRun}", backupPlan.Id, backupPlan.NextRun);
                    }
                    else
                    {
                        var error = await responseback.Content.ReadAsStringAsync();
                        _logger.LogError("Error updating backup plan {BackupPlanId}: {Error}", backupPlan.Id, error);
                    }
                }

                // Create a new BackupJob by calling the API endpoint.
                var response = await _httpClient.PostAsync($"jobs/backup/{backupPlan.Id}/run", null);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Created backup job for plan {PlanId}", backupPlan.Id);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
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
