using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using JobScheduler.Core;
using JobScheduler.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace JobScheduler.Services;

/// <summary>
/// Service for executing SQL queries against Azure SQL Database
/// </summary>
public class SqlQueryJobService : IJobService
{
    private readonly SqlQueryJobConfig _config;
    private readonly ILogger<SqlQueryJobService> _logger;
    private readonly ActivitySource _activitySource;

    public string JobName => _config.JobName;

    public SqlQueryJobService(SqlQueryJobConfig config, ILogger<SqlQueryJobService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activitySource = new ActivitySource("JobScheduler.SqlQueryJob");
    }

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("ExecuteJob", ActivityKind.Internal);
        activity?.SetTag("job.name", _config.JobName);
        activity?.SetTag("job.query_count", _config.Queries.Count);

        _logger.LogInformation("Starting SQL Query Job: {JobName}", _config.JobName);
        _logger.LogInformation("Description: {Description}", _config.Description);
        _logger.LogInformation("Total queries to execute: {QueryCount}", _config.Queries.Count);

        if (!_config.Enabled)
        {
            _logger.LogWarning("Job {JobName} is disabled. Skipping execution.", _config.JobName);
            return true;
        }

        var attempt = 0;
        var maxAttempts = _config.RetryCount + 1;

        while (attempt < maxAttempts)
        {
            attempt++;
            try
            {
                _logger.LogInformation("Execution attempt {Attempt} of {MaxAttempts}", attempt, maxAttempts);

                if (_config.UseTransaction)
                {
                    return await ExecuteWithTransactionAsync(cancellationToken);
                }
                else
                {
                    return await ExecuteWithoutTransactionAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing job on attempt {Attempt} of {MaxAttempts}", attempt, maxAttempts);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                if (attempt < maxAttempts)
                {
                    _logger.LogInformation("Retrying in {DelayMs}ms...", _config.RetryDelayMs);
                    await Task.Delay(_config.RetryDelayMs, cancellationToken);
                }
                else
                {
                    _logger.LogError("All retry attempts exhausted. Job failed.");
                    return false;
                }
            }
        }

        return false;
    }

    private async Task<bool> ExecuteWithTransactionAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_config.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = connection.BeginTransaction();
        
        try
        {
            foreach (var query in _config.Queries)
            {
                await ExecuteQueryAsync(connection, transaction, query, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Transaction committed successfully");
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError("Transaction rolled back due to error");
            throw;
        }
    }

    private async Task<bool> ExecuteWithoutTransactionAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_config.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        foreach (var query in _config.Queries)
        {
            await ExecuteQueryAsync(connection, null, query, cancellationToken);
        }

        _logger.LogInformation("All queries executed successfully");
        return true;
    }

    private async Task ExecuteQueryAsync(
        SqlConnection connection, 
        SqlTransaction? transaction, 
        SqlQuery query, 
        CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("ExecuteQuery", ActivityKind.Internal);
        activity?.SetTag("query.name", query.Name);
        activity?.SetTag("query.is_stored_procedure", query.IsStoredProcedure);

        _logger.LogInformation("Executing query: {QueryName}", query.Name);

        var stopwatch = Stopwatch.StartNew();

        await using var command = new SqlCommand(query.CommandText, connection, transaction)
        {
            CommandTimeout = _config.CommandTimeoutSeconds,
            CommandType = query.IsStoredProcedure ? CommandType.StoredProcedure : CommandType.Text
        };

        // Add parameters with template replacement
        foreach (var param in query.Parameters)
        {
            var value = ReplaceTemplateValues(param.Value?.ToString() ?? string.Empty);
            command.Parameters.AddWithValue($"@{param.Key}", value);
            _logger.LogDebug("Parameter: @{ParamName} = {ParamValue}", param.Key, value);
        }

        try
        {
            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            stopwatch.Stop();

            if (query.LogResultCount)
            {
                _logger.LogInformation(
                    "Query {QueryName} completed successfully. Rows affected: {RowsAffected}. Duration: {DurationMs}ms",
                    query.Name, rowsAffected, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation(
                    "Query {QueryName} completed successfully. Duration: {DurationMs}ms",
                    query.Name, stopwatch.ElapsedMilliseconds);
            }

            activity?.SetTag("query.rows_affected", rowsAffected);
            activity?.SetTag("query.duration_ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error executing query {QueryName} after {DurationMs}ms", 
                query.Name, stopwatch.ElapsedMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Replace template values in parameters
    /// Supported templates:
    /// - {{TODAY}} - Current date
    /// - {{NOW}} - Current datetime
    /// - {{YESTERDAY}} - Yesterday's date
    /// </summary>
    private string ReplaceTemplateValues(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var result = value;
        result = Regex.Replace(result, @"\{\{TODAY\}\}", DateTime.Today.ToString("yyyy-MM-dd"), RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\{\{NOW\}\}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\{\{YESTERDAY\}\}", DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"), RegexOptions.IgnoreCase);

        return result;
    }
}
