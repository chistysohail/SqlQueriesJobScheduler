namespace JobScheduler.Core.Models;

/// <summary>
/// Configuration for SQL Query jobs
/// </summary>
public class SqlQueryJobConfig : JobConfiguration
{
    /// <summary>
    /// Azure SQL Database connection string
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// List of queries to execute
    /// </summary>
    public List<SqlQuery> Queries { get; set; } = new();

    /// <summary>
    /// Whether to execute queries in a transaction
    /// </summary>
    public bool UseTransaction { get; set; } = false;

    /// <summary>
    /// Command timeout in seconds
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Represents a single SQL query to execute
/// </summary>
public class SqlQuery
{
    /// <summary>
    /// Name/identifier for the query
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// SQL command text
    /// </summary>
    public string CommandText { get; set; } = string.Empty;

    /// <summary>
    /// Query parameters (key-value pairs)
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Whether this is a stored procedure
    /// </summary>
    public bool IsStoredProcedure { get; set; } = false;

    /// <summary>
    /// Whether to log the result count
    /// </summary>
    public bool LogResultCount { get; set; } = true;
}
