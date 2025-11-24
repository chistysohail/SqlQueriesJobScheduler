namespace JobScheduler.Core.Models;

/// <summary>
/// Base configuration for all jobs
/// </summary>
public class JobConfiguration
{
    /// <summary>
    /// Name of the job
    /// </summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Description of what the job does
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether the job is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Timeout in seconds for job execution
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Retry count on failure
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Delay between retries in milliseconds
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;
}
