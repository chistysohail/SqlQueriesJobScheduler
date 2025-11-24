# JobScheduler - .NET 9 Console Application for AKS CronJobs

A flexible, extensible .NET 9 console application designed to run scheduled jobs in Azure Kubernetes Service (AKS) as CronJobs. The application supports multiple job types with environment-based configuration, Serilog logging, and Elastic APM monitoring through OpenTelemetry.

## Features

- **Flexible Job Architecture**: Easy to add new job types without modifying core infrastructure
- **Environment-Based Configuration**: Separate configuration files for PRE and PRD environments
- **MS-SQL Query Execution**: Execute multiple SQL queries against Azure SQL Database
- **Structured Logging**: Serilog with console and file sinks
- **Observability**: OpenTelemetry and Elastic APM integration for distributed tracing
- **Kubernetes-Ready**: Designed for AKS CronJob deployment
- **Retry Logic**: Configurable retry mechanism for failed jobs
- **Template Support**: Dynamic parameter replacement (TODAY, NOW, YESTERDAY)

## Project Structure

```
JobScheduler/
├── JobScheduler.csproj          # Project file with NuGet packages
├── Program.cs                   # Main entry point with DI and configuration
├── Dockerfile                   # Container image definition
├── appsettings.json            # Global configuration (Serilog, OpenTelemetry, Elastic APM)
├── appsettings.Development.json
├── appsettings.Production.json
├── Configuration/              # Job-specific configurations
│   ├── PRE/
│   │   └── SqlQueryJob.json
│   └── PRD/
│       └── SqlQueryJob.json
├── Core/
│   ├── IJobService.cs          # Job service interface
│   ├── JobFactory.cs           # Factory for creating job instances
│   └── Models/
│       ├── JobConfiguration.cs
│       └── SqlQueryJobConfig.cs
├── Services/
│   └── SqlQueryJobService.cs   # SQL query job implementation
└── K8s/                        # Kubernetes manifests
    ├── configmap-pre.yaml
    ├── secret-pre.yaml
    ├── cronjob-pre.yaml
    └── cronjob-prd.yaml
```

## Prerequisites

- .NET 9 SDK
- Docker (for containerization)
- Azure Container Registry (ACR) or other container registry
- Azure Kubernetes Service (AKS) cluster
- Azure SQL Database
- Elastic APM Server (optional)
- OpenTelemetry Collector (optional)

## Configuration

### Global Configuration (appsettings.json)

The `appsettings.json` file contains global settings for:

- **Serilog**: Logging configuration with console and file sinks
- **OpenTelemetry**: Service name, version, and OTLP endpoint
- **Elastic APM**: Server URL, secret token, and environment settings

### Job-Specific Configuration

Each job has its own JSON configuration file stored in `Configuration/{ENV}/{JobName}.json`.

**Example: Configuration/PRD/SqlQueryJob.json**

```json
{
  "JobName": "SqlQueryJob",
  "Description": "Executes daily SQL queries against Azure SQL Database",
  "Enabled": true,
  "TimeoutSeconds": 600,
  "RetryCount": 3,
  "RetryDelayMs": 10000,
  "ConnectionString": "Server=tcp:your-server.database.windows.net,1433;...",
  "UseTransaction": false,
  "CommandTimeoutSeconds": 120,
  "Queries": [
    {
      "Name": "Query1_UpdateDailyStats",
      "CommandText": "UPDATE DailyStats SET ProcessedDate = GETDATE() WHERE Date = @TargetDate",
      "Parameters": {
        "TargetDate": "{{TODAY}}"
      },
      "IsStoredProcedure": false,
      "LogResultCount": true
    }
  ]
}
```

### Template Variables

The following template variables are supported in query parameters:

- `{{TODAY}}` - Current date (yyyy-MM-dd)
- `{{NOW}}` - Current datetime (yyyy-MM-dd HH:mm:ss)
- `{{YESTERDAY}}` - Yesterday's date (yyyy-MM-dd)

## Local Development

### Build the Project

```bash
dotnet restore
dotnet build
```

### Run Locally

```bash
dotnet run -- --job SqlQueryJob --env PRE
```

### Test with Different Jobs

```bash
# Run SQL Query Job for PRE environment
dotnet run -- --job SqlQueryJob --env PRE

# Run SQL Query Job for PRD environment
dotnet run -- --job SqlQueryJob --env PRD
```

## Docker Build

### Build Docker Image

```bash
docker build -t jobscheduler:latest .
```

### Test Docker Image Locally

```bash
docker run --rm \
  -e DOTNET_ENVIRONMENT=Production \
  jobscheduler:latest --job SqlQueryJob --env PRE
```

### Push to Azure Container Registry

```bash
# Login to ACR
az acr login --name your-acr-name

# Tag image
docker tag jobscheduler:latest your-acr.azurecr.io/jobscheduler:latest

# Push image
docker push your-acr.azurecr.io/jobscheduler:latest
```

## Kubernetes Deployment

### 1. Create Secrets

Update the secret files with actual credentials:

```bash
# Edit the secret file
vi K8s/secret-pre.yaml

# Apply the secret
kubectl apply -f K8s/secret-pre.yaml
```

**Important**: Never commit actual secrets to version control. Use Azure Key Vault or Kubernetes Secrets management.

### 2. Create ConfigMaps

```bash
kubectl apply -f K8s/configmap-pre.yaml
```

### 3. Deploy CronJob

```bash
# For PRE environment
kubectl apply -f K8s/cronjob-pre.yaml

# For PRD environment
kubectl apply -f K8s/cronjob-prd.yaml
```

### 4. Verify Deployment

```bash
# List CronJobs
kubectl get cronjobs

# Check job history
kubectl get jobs

# View pod logs
kubectl logs -l job=sqlqueryjob

# Manually trigger a job (for testing)
kubectl create job --from=cronjob/sqlqueryjob-pre manual-test-1
```

## Adding New Job Types

### Step 1: Create Configuration Model

Create a new model in `Core/Models/`:

```csharp
public class ApiCallJobConfig : JobConfiguration
{
    public string ApiUrl { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public Dictionary<string, string> Headers { get; set; } = new();
}
```

### Step 2: Implement Job Service

Create a new service in `Services/`:

```csharp
public class ApiCallJobService : IJobService
{
    private readonly ApiCallJobConfig _config;
    private readonly ILogger<ApiCallJobService> _logger;

    public string JobName => _config.JobName;

    public ApiCallJobService(ApiCallJobConfig config, ILogger<ApiCallJobService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Implementation here
        return true;
    }
}
```

### Step 3: Register in JobFactory

Update `Core/JobFactory.cs`:

```csharp
return jobName switch
{
    "SqlQueryJob" => CreateSqlQueryJob(configJson),
    "ApiCallJob" => CreateApiCallJob(configJson),  // Add this line
    _ => throw new NotSupportedException($"Job type '{jobName}' is not supported")
};
```

### Step 4: Create Configuration Files

Create `Configuration/PRE/ApiCallJob.json` and `Configuration/PRD/ApiCallJob.json`.

### Step 5: Create Kubernetes Manifests

Create ConfigMap and CronJob YAML files for the new job.

## Monitoring and Observability

### Serilog Logging

Logs are written to:
- **Console**: For Kubernetes log aggregation
- **File**: `/app/logs/jobscheduler-{Date}.log` (inside container)

Log levels can be configured per environment in `appsettings.{Environment}.json`.

### Elastic APM

Elastic APM tracks:
- Job execution duration
- SQL query performance
- Errors and exceptions
- Custom transactions and spans

Configure Elastic APM via environment variables:
- `ELASTIC_APM_SERVER_URL`
- `ELASTIC_APM_SECRET_TOKEN`
- `ELASTIC_APM_SERVICE_NAME`
- `ELASTIC_APM_ENVIRONMENT`

### OpenTelemetry

OpenTelemetry provides:
- Distributed tracing
- SQL instrumentation
- Custom activity sources
- OTLP export to collectors

Configure via `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable.

## Troubleshooting

### Job Not Running

```bash
# Check CronJob status
kubectl describe cronjob sqlqueryjob-pre

# Check if job was created
kubectl get jobs

# Check pod status
kubectl get pods -l job=sqlqueryjob
```

### View Logs

```bash
# Get logs from the most recent job
kubectl logs -l job=sqlqueryjob --tail=100

# Get logs from a specific pod
kubectl logs <pod-name>
```

### Connection String Issues

Ensure the connection string in the Secret is correctly formatted:

```
Server=tcp:your-server.database.windows.net,1433;Initial Catalog=YourDatabase;User ID=your-user;Password=your-password;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

### ConfigMap Not Mounted

```bash
# Verify ConfigMap exists
kubectl get configmap jobscheduler-config-pre -o yaml

# Check volume mounts in pod
kubectl describe pod <pod-name>
```

## Security Best Practices

1. **Never commit secrets** to version control
2. Use **Azure Key Vault** or **Kubernetes Secrets** for sensitive data
3. Enable **Azure AD authentication** for SQL Database (Managed Identity)
4. Use **least privilege** RBAC roles
5. Enable **Pod Security Standards** in AKS
6. Regularly **rotate credentials**
7. Use **private container registries**

## Performance Tuning

### Resource Limits

Adjust resource requests and limits based on workload:

```yaml
resources:
  requests:
    memory: "512Mi"
    cpu: "200m"
  limits:
    memory: "1Gi"
    cpu: "1000m"
```

### SQL Query Optimization

- Use appropriate indexes
- Optimize query execution plans
- Set appropriate command timeouts
- Consider using stored procedures for complex operations

### Retry Configuration

Adjust retry settings based on job characteristics:

```json
{
  "RetryCount": 3,
  "RetryDelayMs": 10000
}
```

## Future Enhancements

Potential job types to add:

1. **ApiCallJob**: Call REST APIs with configurable endpoints
2. **BlobProcessingJob**: Process files from Azure Blob Storage
3. **EventHubJob**: Consume messages from Azure Event Hub
4. **DataSyncJob**: Sync data between systems
5. **ReportGenerationJob**: Generate and distribute reports

## License

This project is provided as-is for internal use.

## Support

For issues or questions, contact the DevOps team or create an issue in the repository.