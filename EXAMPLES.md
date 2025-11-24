# Job Examples

This document provides examples of different job types you can implement with the JobScheduler framework.

## Example 1: API Call Job

### Configuration Model

Create `Core/Models/ApiCallJobConfig.cs`:

```csharp
namespace JobScheduler.Core.Models;

public class ApiCallJobConfig : JobConfiguration
{
    public string ApiUrl { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? RequestBody { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public bool ValidateSslCertificate { get; set; } = true;
}
```

### Service Implementation

Create `Services/ApiCallJobService.cs`:

```csharp
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using JobScheduler.Core;
using JobScheduler.Core.Models;
using Microsoft.Extensions.Logging;

namespace JobScheduler.Services;

public class ApiCallJobService : IJobService
{
    private readonly ApiCallJobConfig _config;
    private readonly ILogger<ApiCallJobService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ActivitySource _activitySource;

    public string JobName => _config.JobName;

    public ApiCallJobService(ApiCallJobConfig config, ILogger<ApiCallJobService> logger, HttpClient httpClient)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClient;
        _activitySource = new ActivitySource("JobScheduler.ApiCallJob");
    }

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("ExecuteApiCall", ActivityKind.Client);
        activity?.SetTag("http.url", _config.ApiUrl);
        activity?.SetTag("http.method", _config.HttpMethod);

        _logger.LogInformation("Starting API Call Job: {JobName}", _config.JobName);
        _logger.LogInformation("API URL: {ApiUrl}", _config.ApiUrl);
        _logger.LogInformation("HTTP Method: {HttpMethod}", _config.HttpMethod);

        try
        {
            var request = new HttpRequestMessage(new HttpMethod(_config.HttpMethod), _config.ApiUrl);

            // Add headers
            foreach (var header in _config.Headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }

            // Add request body if provided
            if (!string.IsNullOrEmpty(_config.RequestBody))
            {
                request.Content = new StringContent(_config.RequestBody, Encoding.UTF8, "application/json");
            }

            // Send request
            var response = await _httpClient.SendAsync(request, cancellationToken);

            activity?.SetTag("http.status_code", (int)response.StatusCode);

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation("API call successful. Status: {StatusCode}", response.StatusCode);
                _logger.LogDebug("Response: {ResponseBody}", responseBody);
                return true;
            }
            else
            {
                _logger.LogError("API call failed. Status: {StatusCode}", response.StatusCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing API call");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
    }
}
```

### Configuration File

Create `Configuration/PRD/ApiCallJob.json`:

```json
{
  "JobName": "ApiCallJob",
  "Description": "Calls external API to trigger data sync",
  "Enabled": true,
  "TimeoutSeconds": 300,
  "RetryCount": 3,
  "RetryDelayMs": 5000,
  "ApiUrl": "https://api.example.com/v1/sync",
  "HttpMethod": "POST",
  "Headers": {
    "Authorization": "Bearer YOUR_API_TOKEN",
    "Content-Type": "application/json"
  },
  "RequestBody": "{\"syncDate\": \"{{TODAY}}\", \"fullSync\": false}",
  "TimeoutSeconds": 30,
  "ValidateSslCertificate": true
}
```

### Update JobFactory

Add to `Core/JobFactory.cs`:

```csharp
private IJobService CreateApiCallJob(string configJson)
{
    var config = JsonSerializer.Deserialize<ApiCallJobConfig>(configJson, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });

    if (config == null)
    {
        throw new InvalidOperationException("Failed to deserialize ApiCallJobConfig");
    }

    var logger = _serviceProvider.GetService(typeof(ILogger<ApiCallJobService>)) as ILogger<ApiCallJobService>;
    var httpClient = _serviceProvider.GetService(typeof(HttpClient)) as HttpClient;
    return new ApiCallJobService(config, logger!, httpClient!);
}
```

## Example 2: Blob Processing Job

### Configuration Model

Create `Core/Models/BlobProcessingJobConfig.cs`:

```csharp
namespace JobScheduler.Core.Models;

public class BlobProcessingJobConfig : JobConfiguration
{
    public string StorageConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string BlobPrefix { get; set; } = string.Empty;
    public string ProcessingType { get; set; } = "CSV"; // CSV, JSON, XML
    public bool DeleteAfterProcessing { get; set; } = false;
    public string? DestinationConnectionString { get; set; }
}
```

### Service Implementation

Create `Services/BlobProcessingJobService.cs`:

```csharp
using System.Diagnostics;
using Azure.Storage.Blobs;
using JobScheduler.Core;
using JobScheduler.Core.Models;
using Microsoft.Extensions.Logging;

namespace JobScheduler.Services;

public class BlobProcessingJobService : IJobService
{
    private readonly BlobProcessingJobConfig _config;
    private readonly ILogger<BlobProcessingJobService> _logger;
    private readonly ActivitySource _activitySource;

    public string JobName => _config.JobName;

    public BlobProcessingJobService(BlobProcessingJobConfig config, ILogger<BlobProcessingJobService> logger)
    {
        _config = config;
        _logger = logger;
        _activitySource = new ActivitySource("JobScheduler.BlobProcessingJob");
    }

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity("ProcessBlobs", ActivityKind.Internal);
        activity?.SetTag("container.name", _config.ContainerName);

        _logger.LogInformation("Starting Blob Processing Job: {JobName}", _config.JobName);

        try
        {
            var blobServiceClient = new BlobServiceClient(_config.StorageConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(_config.ContainerName);

            var blobs = containerClient.GetBlobsAsync(prefix: _config.BlobPrefix, cancellationToken: cancellationToken);
            var processedCount = 0;

            await foreach (var blob in blobs)
            {
                _logger.LogInformation("Processing blob: {BlobName}", blob.Name);

                var blobClient = containerClient.GetBlobClient(blob.Name);
                var downloadResult = await blobClient.DownloadContentAsync(cancellationToken);
                var content = downloadResult.Value.Content.ToString();

                // Process based on type
                switch (_config.ProcessingType.ToUpper())
                {
                    case "CSV":
                        ProcessCsv(content);
                        break;
                    case "JSON":
                        ProcessJson(content);
                        break;
                    case "XML":
                        ProcessXml(content);
                        break;
                }

                if (_config.DeleteAfterProcessing)
                {
                    await blobClient.DeleteAsync(cancellationToken: cancellationToken);
                    _logger.LogInformation("Deleted blob: {BlobName}", blob.Name);
                }

                processedCount++;
            }

            _logger.LogInformation("Processed {Count} blobs successfully", processedCount);
            activity?.SetTag("blobs.processed", processedCount);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing blobs");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
    }

    private void ProcessCsv(string content)
    {
        // CSV processing logic
        _logger.LogDebug("Processing CSV content");
    }

    private void ProcessJson(string content)
    {
        // JSON processing logic
        _logger.LogDebug("Processing JSON content");
    }

    private void ProcessXml(string content)
    {
        // XML processing logic
        _logger.LogDebug("Processing XML content");
    }
}
```

### Configuration File

Create `Configuration/PRD/BlobProcessingJob.json`:

```json
{
  "JobName": "BlobProcessingJob",
  "Description": "Processes CSV files from Azure Blob Storage",
  "Enabled": true,
  "TimeoutSeconds": 600,
  "RetryCount": 2,
  "RetryDelayMs": 5000,
  "StorageConnectionString": "DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=yourkey;EndpointSuffix=core.windows.net",
  "ContainerName": "incoming-data",
  "BlobPrefix": "daily-reports/",
  "ProcessingType": "CSV",
  "DeleteAfterProcessing": true
}
```

## Example 3: Data Sync Job

### Configuration Model

Create `Core/Models/DataSyncJobConfig.cs`:

```csharp
namespace JobScheduler.Core.Models;

public class DataSyncJobConfig : JobConfiguration
{
    public string SourceConnectionString { get; set; } = string.Empty;
    public string DestinationConnectionString { get; set; } = string.Empty;
    public List<TableSync> Tables { get; set; } = new();
    public bool TruncateBeforeSync { get; set; } = false;
    public int BatchSize { get; set; } = 1000;
}

public class TableSync
{
    public string SourceTable { get; set; } = string.Empty;
    public string DestinationTable { get; set; } = string.Empty;
    public string? WhereClause { get; set; }
    public List<string> Columns { get; set; } = new();
}
```

### Configuration File

Create `Configuration/PRD/DataSyncJob.json`:

```json
{
  "JobName": "DataSyncJob",
  "Description": "Syncs data from source to destination database",
  "Enabled": true,
  "TimeoutSeconds": 1800,
  "RetryCount": 1,
  "RetryDelayMs": 10000,
  "SourceConnectionString": "Server=source-server.database.windows.net;Database=SourceDB;...",
  "DestinationConnectionString": "Server=dest-server.database.windows.net;Database=DestDB;...",
  "TruncateBeforeSync": true,
  "BatchSize": 5000,
  "Tables": [
    {
      "SourceTable": "Sales",
      "DestinationTable": "Sales_Archive",
      "WhereClause": "SaleDate >= DATEADD(day, -7, GETDATE())",
      "Columns": ["SaleId", "CustomerId", "Amount", "SaleDate"]
    },
    {
      "SourceTable": "Customers",
      "DestinationTable": "Customers_Archive",
      "Columns": ["CustomerId", "Name", "Email", "CreatedDate"]
    }
  ]
}
```

## Example 4: Report Generation Job

### Configuration Model

Create `Core/Models/ReportJobConfig.cs`:

```csharp
namespace JobScheduler.Core.Models;

public class ReportJobConfig : JobConfiguration
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ReportQuery { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = "CSV"; // CSV, Excel, PDF
    public string OutputPath { get; set; } = string.Empty;
    public EmailConfig? EmailConfig { get; set; }
}

public class EmailConfig
{
    public string SmtpServer { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string FromAddress { get; set; } = string.Empty;
    public List<string> ToAddresses { get; set; } = new();
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
```

### Configuration File

Create `Configuration/PRD/DailyReportJob.json`:

```json
{
  "JobName": "DailyReportJob",
  "Description": "Generates and emails daily sales report",
  "Enabled": true,
  "TimeoutSeconds": 300,
  "RetryCount": 2,
  "RetryDelayMs": 5000,
  "ConnectionString": "Server=your-server.database.windows.net;...",
  "ReportQuery": "SELECT * FROM SalesSummary WHERE ReportDate = '{{TODAY}}'",
  "OutputFormat": "Excel",
  "OutputPath": "/tmp/reports/",
  "EmailConfig": {
    "SmtpServer": "smtp.office365.com",
    "SmtpPort": 587,
    "FromAddress": "reports@company.com",
    "ToAddresses": ["manager@company.com", "analyst@company.com"],
    "Subject": "Daily Sales Report - {{TODAY}}",
    "Body": "Please find attached the daily sales report."
  }
}
```

## Example 5: Multi-Environment Configuration

### Development Environment

`Configuration/DEV/SqlQueryJob.json`:

```json
{
  "JobName": "SqlQueryJob",
  "Enabled": true,
  "TimeoutSeconds": 120,
  "RetryCount": 1,
  "ConnectionString": "Server=localhost;Database=TestDB;Integrated Security=true;",
  "Queries": [
    {
      "Name": "TestQuery",
      "CommandText": "SELECT COUNT(*) FROM TestTable",
      "Parameters": {}
    }
  ]
}
```

### Pre-Production Environment

`Configuration/PRE/SqlQueryJob.json`:

```json
{
  "JobName": "SqlQueryJob",
  "Enabled": true,
  "TimeoutSeconds": 300,
  "RetryCount": 2,
  "ConnectionString": "Server=pre-server.database.windows.net;...",
  "Queries": [
    {
      "Name": "Query1",
      "CommandText": "EXEC sp_ProcessDailyData @Date",
      "Parameters": { "Date": "{{TODAY}}" },
      "IsStoredProcedure": true
    }
  ]
}
```

### Production Environment

`Configuration/PRD/SqlQueryJob.json`:

```json
{
  "JobName": "SqlQueryJob",
  "Enabled": true,
  "TimeoutSeconds": 600,
  "RetryCount": 3,
  "RetryDelayMs": 10000,
  "ConnectionString": "Server=prd-server.database.windows.net;...",
  "UseTransaction": true,
  "Queries": [
    {
      "Name": "Query1",
      "CommandText": "EXEC sp_ProcessDailyData @Date",
      "Parameters": { "Date": "{{TODAY}}" },
      "IsStoredProcedure": true
    },
    {
      "Name": "Query2",
      "CommandText": "EXEC sp_UpdateMetrics",
      "Parameters": {},
      "IsStoredProcedure": true
    }
  ]
}
```

## Testing Examples

### Local Testing

```bash
# Test SQL Query Job
dotnet run -- --job SqlQueryJob --env PRE

# Test API Call Job
dotnet run -- --job ApiCallJob --env PRE

# Test Blob Processing Job
dotnet run -- --job BlobProcessingJob --env PRE
```

### Docker Testing

```bash
# Build image
docker build -t jobscheduler:test .

# Run SQL Query Job
docker run --rm \
  -e DOTNET_ENVIRONMENT=Production \
  jobscheduler:test --job SqlQueryJob --env PRE

# Run with custom configuration
docker run --rm \
  -v $(pwd)/Configuration:/app/Configuration \
  jobscheduler:test --job SqlQueryJob --env PRE
```

### Kubernetes Testing

```bash
# Create test job
kubectl create job --from=cronjob/sqlqueryjob-pre test-$(date +%s)

# Watch logs
kubectl logs -l job=sqlqueryjob -f

# Check job status
kubectl get jobs
kubectl describe job <job-name>
```

## Advanced Patterns

### Conditional Execution

Add logic to skip execution based on conditions:

```csharp
public async Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
{
    // Check if today is a business day
    if (DateTime.Today.DayOfWeek == DayOfWeek.Saturday || 
        DateTime.Today.DayOfWeek == DayOfWeek.Sunday)
    {
        _logger.LogInformation("Skipping execution on weekend");
        return true;
    }

    // Execute job
    return await DoWorkAsync(cancellationToken);
}
```

### Parallel Processing

Process multiple items in parallel:

```csharp
var tasks = items.Select(item => ProcessItemAsync(item, cancellationToken));
await Task.WhenAll(tasks);
```

### Progress Reporting

Report progress during long-running operations:

```csharp
var total = items.Count;
var processed = 0;

foreach (var item in items)
{
    await ProcessItemAsync(item, cancellationToken);
    processed++;
    
    if (processed % 100 == 0)
    {
        _logger.LogInformation("Progress: {Processed}/{Total} ({Percentage}%)", 
            processed, total, (processed * 100) / total);
    }
}
```

## Conclusion

These examples demonstrate the flexibility of the JobScheduler framework. You can easily add new job types by:

1. Creating a configuration model
2. Implementing the IJobService interface
3. Registering in JobFactory
4. Creating configuration files
5. Deploying to Kubernetes

For more information, see the main README.md and DEPLOYMENT.md files.
