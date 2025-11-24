using System.Text.Json;
using JobScheduler.Core.Models;
using JobScheduler.Services;
using Microsoft.Extensions.Logging;

namespace JobScheduler.Core;

/// <summary>
/// Factory for creating job service instances based on configuration
/// </summary>
public class JobFactory
{
    private readonly ILogger<JobFactory> _logger;
    private readonly IServiceProvider _serviceProvider;

    public JobFactory(ILogger<JobFactory> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Create a job service instance based on job name and environment
    /// </summary>
    /// <param name="jobName">Name of the job</param>
    /// <param name="environment">Environment (PRE/PRD)</param>
    /// <returns>Job service instance</returns>
    public IJobService? CreateJob(string jobName, string environment)
    {
        _logger.LogInformation("Creating job: {JobName} for environment: {Environment}", jobName, environment);

        // Load job configuration from file
        var configPath = Path.Combine("Configuration", environment, $"{jobName}.json");
        
        if (!File.Exists(configPath))
        {
            _logger.LogError("Configuration file not found: {ConfigPath}", configPath);
            return null;
        }

        var configJson = File.ReadAllText(configPath);
        
        // Determine job type and create appropriate service
        return jobName switch
        {
            "SqlQueryJob" => CreateSqlQueryJob(configJson),
            // Add more job types here as they are implemented
            // "ApiCallJob" => CreateApiCallJob(configJson),
            // "BlobProcessingJob" => CreateBlobProcessingJob(configJson),
            _ => throw new NotSupportedException($"Job type '{jobName}' is not supported")
        };
    }

    private IJobService CreateSqlQueryJob(string configJson)
    {
        var config = JsonSerializer.Deserialize<SqlQueryJobConfig>(configJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize SqlQueryJobConfig");
        }

        var logger = _serviceProvider.GetService(typeof(ILogger<SqlQueryJobService>)) as ILogger<SqlQueryJobService>;
        return new SqlQueryJobService(config, logger!);
    }

    // Future job creation methods
    // private IJobService CreateApiCallJob(string configJson) { ... }
    // private IJobService CreateBlobProcessingJob(string configJson) { ... }
}
