namespace JobScheduler.Core;

/// <summary>
/// Interface for all job services
/// </summary>
public interface IJobService
{
    /// <summary>
    /// Execute the job asynchronously
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the job name
    /// </summary>
    string JobName { get; }
}
