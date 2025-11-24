using System.Diagnostics;
using Elastic.Apm;
using Elastic.Apm.Api;
using Elastic.Apm.NetCoreAll;
using JobScheduler.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace JobScheduler;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Parse command line arguments
        var jobName = GetArgument(args, "--job");
        var environment = GetArgument(args, "--env") ?? "PRE";

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.WithProperty("JobName", jobName ?? "Unknown")
            .Enrich.WithProperty("Environment", environment)
            .CreateLogger();

        try
        {
            Log.Information("========================================");
            Log.Information("JobScheduler Starting");
            Log.Information("Job: {JobName}", jobName);
            Log.Information("Environment: {Environment}", environment);
            Log.Information("========================================");

            // Validate arguments
            if (string.IsNullOrEmpty(jobName))
            {
                Log.Error("Job name is required. Use --job argument.");
                Log.Information("Usage: JobScheduler --job <JobName> --env <Environment>");
                Log.Information("Example: JobScheduler --job SqlQueryJob --env PRD");
                return 1;
            }

            // Build host
            var host = CreateHostBuilder(args, configuration, environment).Build();

            // Execute job
            var exitCode = await ExecuteJobAsync(host, jobName, environment);

            Log.Information("========================================");
            Log.Information("JobScheduler Completed");
            Log.Information("Exit Code: {ExitCode}", exitCode);
            Log.Information("========================================");

            return exitCode;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    static IHostBuilder CreateHostBuilder(string[] args, IConfiguration configuration, string environment)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Register services
                services.AddSingleton<JobFactory>();

                // Configure OpenTelemetry
                var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "JobScheduler";
                var serviceVersion = configuration["OpenTelemetry:ServiceVersion"] ?? "1.0.0";
                var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

                services.AddOpenTelemetry()
                    .ConfigureResource(resource => resource
                        .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                        .AddAttributes(new Dictionary<string, object>
                        {
                            ["deployment.environment"] = environment
                        }))
                    .WithTracing(tracing =>
                    {
                        tracing
                            .AddSource("JobScheduler.*")
                            .AddSqlClientInstrumentation(options =>
                            {
                                options.SetDbStatementForText = true;
                                options.RecordException = true;
                            })
                            .AddSource("Elastic.Apm");

                        // Add OTLP exporter if endpoint is configured
                        if (!string.IsNullOrEmpty(otlpEndpoint))
                        {
                            tracing.AddOtlpExporter(options =>
                            {
                                options.Endpoint = new Uri(otlpEndpoint);
                            });
                        }
                    });

                // Elastic APM is configured via environment variables
                // No additional service registration needed for NetCoreAll package
            })
            .UseSerilog();
    }

    static async Task<int> ExecuteJobAsync(IHost host, string jobName, string environment)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();
        var jobFactory = services.GetRequiredService<JobFactory>();

        // Create activity source for tracing
        using var activitySource = new ActivitySource("JobScheduler.Main");
        using var activity = activitySource.StartActivity("ExecuteJob", ActivityKind.Internal);
        activity?.SetTag("job.name", jobName);
        activity?.SetTag("job.environment", environment);

        ITransaction? apmTransaction = null;
        
        try
        {
            // Start Elastic APM transaction if available
            if (Agent.IsConfigured)
            {
                apmTransaction = Agent.Tracer.StartTransaction(jobName, "job");
                apmTransaction.SetLabel("environment", environment);
            }

            // Create and execute job
            var job = jobFactory.CreateJob(jobName, environment);
            
            if (job == null)
            {
                logger.LogError("Failed to create job: {JobName}", jobName);
                activity?.SetStatus(ActivityStatusCode.Error, "Failed to create job");
                apmTransaction?.CaptureError("Failed to create job", "Job creation failed", null);
                return 1;
            }

            logger.LogInformation("Job created successfully: {JobName}", job.JobName);

            // Execute the job
            var success = await job.ExecuteAsync();

            if (success)
            {
                logger.LogInformation("Job executed successfully");
                activity?.SetStatus(ActivityStatusCode.Ok);
                if (apmTransaction != null) apmTransaction.Result = "success";
                return 0;
            }
            else
            {
                logger.LogError("Job execution failed");
                activity?.SetStatus(ActivityStatusCode.Error, "Job execution failed");
                if (apmTransaction != null) apmTransaction.Result = "failure";
                return 1;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception during job execution");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            apmTransaction?.CaptureException(ex);
            if (apmTransaction != null) apmTransaction.Result = "error";
            return 1;
        }
        finally
        {
            apmTransaction?.End();
        }
    }

    static string? GetArgument(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
