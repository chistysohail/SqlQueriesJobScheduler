# Deployment Guide

This guide provides step-by-step instructions for deploying the JobScheduler application to Azure Kubernetes Service (AKS).

## Prerequisites

Before deploying, ensure you have:

- Azure CLI installed and configured
- kubectl installed and configured
- Access to Azure Container Registry (ACR)
- Access to AKS cluster
- Azure SQL Database provisioned
- Elastic APM Server (optional)

## Step 1: Build and Push Docker Image

### 1.1 Build the Docker Image

```bash
cd JobScheduler
docker build -t jobscheduler:1.0.0 .
```

### 1.2 Login to Azure Container Registry

```bash
az acr login --name your-acr-name
```

### 1.3 Tag and Push Image

```bash
# Tag the image
docker tag jobscheduler:1.0.0 your-acr.azurecr.io/jobscheduler:1.0.0
docker tag jobscheduler:1.0.0 your-acr.azurecr.io/jobscheduler:latest

# Push to ACR
docker push your-acr.azurecr.io/jobscheduler:1.0.0
docker push your-acr.azurecr.io/jobscheduler:latest
```

## Step 2: Configure AKS Cluster

### 2.1 Connect to AKS Cluster

```bash
az aks get-credentials --resource-group your-rg --name your-aks-cluster
```

### 2.2 Verify Connection

```bash
kubectl get nodes
kubectl get namespaces
```

### 2.3 Create Namespace (if needed)

```bash
# For PRE environment
kubectl create namespace pre

# For PRD environment
kubectl create namespace production
```

### 2.4 Configure ACR Access

If using private ACR, create image pull secret:

```bash
kubectl create secret docker-registry acr-secret \
  --namespace=default \
  --docker-server=your-acr.azurecr.io \
  --docker-username=<acr-username> \
  --docker-password=<acr-password>
```

Or use AKS-ACR integration:

```bash
az aks update -n your-aks-cluster -g your-rg --attach-acr your-acr-name
```

## Step 3: Configure Secrets

### 3.1 Prepare Secret Values

Create a file `secrets-pre.env` with actual values:

```bash
SQL_CONNECTION_STRING="Server=tcp:your-server-pre.database.windows.net,1433;Initial Catalog=YourDatabase;User ID=your-user;Password=YOUR_PASSWORD;Encrypt=True;"
ELASTIC_APM_SERVER_URL="https://your-apm-server:8200"
ELASTIC_APM_SECRET_TOKEN="YOUR_APM_TOKEN"
OTEL_EXPORTER_OTLP_ENDPOINT="http://your-otel-collector:4317"
```

### 3.2 Create Secret from File

```bash
kubectl create secret generic jobscheduler-secrets-pre \
  --from-env-file=secrets-pre.env \
  --namespace=default
```

Or use the YAML file (after updating values):

```bash
# Edit K8s/secret-pre.yaml with actual values
vi K8s/secret-pre.yaml

# Apply the secret
kubectl apply -f K8s/secret-pre.yaml
```

### 3.3 Verify Secret

```bash
kubectl get secret jobscheduler-secrets-pre -o yaml
```

**Important**: Delete the `secrets-pre.env` file after creating the secret to avoid exposing credentials.

## Step 4: Deploy ConfigMaps

### 4.1 Review ConfigMap

```bash
cat K8s/configmap-pre.yaml
```

### 4.2 Update Job Configuration

Edit the ConfigMap to match your SQL queries:

```bash
vi K8s/configmap-pre.yaml
```

### 4.3 Apply ConfigMap

```bash
kubectl apply -f K8s/configmap-pre.yaml
```

### 4.4 Verify ConfigMap

```bash
kubectl get configmap jobscheduler-config-pre -o yaml
kubectl get configmap jobscheduler-sqlqueryjob-pre -o yaml
```

## Step 5: Deploy CronJob

### 5.1 Review CronJob Manifest

```bash
cat K8s/cronjob-pre.yaml
```

### 5.2 Update Image Reference

Edit the CronJob YAML to reference your ACR:

```bash
vi K8s/cronjob-pre.yaml
```

Update the image line:

```yaml
image: your-acr.azurecr.io/jobscheduler:latest
```

### 5.3 Update Schedule (if needed)

The default schedule is midnight daily (`0 0 * * *`). Adjust if needed:

```yaml
schedule: "0 0 * * *"  # Midnight daily
# schedule: "0 */6 * * *"  # Every 6 hours
# schedule: "30 2 * * *"  # 2:30 AM daily
```

### 5.4 Apply CronJob

```bash
kubectl apply -f K8s/cronjob-pre.yaml
```

### 5.5 Verify CronJob

```bash
kubectl get cronjob sqlqueryjob-pre
kubectl describe cronjob sqlqueryjob-pre
```

## Step 6: Testing

### 6.1 Manually Trigger Job

Create a one-time job from the CronJob:

```bash
kubectl create job --from=cronjob/sqlqueryjob-pre manual-test-$(date +%s)
```

### 6.2 Monitor Job Execution

```bash
# Watch job status
kubectl get jobs -w

# Get pods
kubectl get pods -l job=sqlqueryjob

# View logs
kubectl logs -l job=sqlqueryjob --tail=100 -f
```

### 6.3 Check Job Status

```bash
# Get job details
kubectl describe job <job-name>

# Check if job completed successfully
kubectl get job <job-name> -o jsonpath='{.status.succeeded}'
```

### 6.4 Verify Database Changes

Connect to your Azure SQL Database and verify that the queries executed successfully.

## Step 7: Production Deployment

### 7.1 Create Production Secrets

```bash
kubectl create secret generic jobscheduler-secrets-prd \
  --from-env-file=secrets-prd.env \
  --namespace=production
```

### 7.2 Create Production ConfigMaps

Create `K8s/configmap-prd.yaml` similar to PRE environment:

```bash
kubectl apply -f K8s/configmap-prd.yaml -n production
```

### 7.3 Deploy Production CronJob

```bash
kubectl apply -f K8s/cronjob-prd.yaml -n production
```

### 7.4 Verify Production Deployment

```bash
kubectl get cronjob sqlqueryjob-prd -n production
kubectl describe cronjob sqlqueryjob-prd -n production
```

## Step 8: Monitoring and Maintenance

### 8.1 View CronJob Schedule

```bash
kubectl get cronjob sqlqueryjob-pre
```

### 8.2 View Job History

```bash
kubectl get jobs -l app=jobscheduler
```

### 8.3 View Logs

```bash
# Recent logs
kubectl logs -l job=sqlqueryjob --tail=100

# Logs from specific job
kubectl logs job/<job-name>

# Stream logs
kubectl logs -l job=sqlqueryjob -f
```

### 8.4 Check Elastic APM

Navigate to your Elastic APM dashboard to view:
- Transaction traces
- SQL query performance
- Error rates
- Service dependencies

### 8.5 Clean Up Old Jobs

Kubernetes automatically cleans up old jobs based on `successfulJobsHistoryLimit` and `failedJobsHistoryLimit`. You can also manually delete:

```bash
kubectl delete job <job-name>
```

## Step 9: Updating the Application

### 9.1 Build New Version

```bash
docker build -t jobscheduler:1.1.0 .
docker tag jobscheduler:1.1.0 your-acr.azurecr.io/jobscheduler:1.1.0
docker tag jobscheduler:1.1.0 your-acr.azurecr.io/jobscheduler:latest
docker push your-acr.azurecr.io/jobscheduler:1.1.0
docker push your-acr.azurecr.io/jobscheduler:latest
```

### 9.2 Update CronJob

If using `latest` tag, the next scheduled run will use the new image. For immediate update:

```bash
# Delete existing CronJob
kubectl delete cronjob sqlqueryjob-pre

# Reapply with new image
kubectl apply -f K8s/cronjob-pre.yaml
```

### 9.3 Test New Version

```bash
kubectl create job --from=cronjob/sqlqueryjob-pre test-v1.1.0
kubectl logs -l job=sqlqueryjob --tail=100 -f
```

## Step 10: Rollback

### 10.1 Rollback to Previous Image

Update the CronJob to use a specific version:

```bash
kubectl set image cronjob/sqlqueryjob-pre jobscheduler=your-acr.azurecr.io/jobscheduler:1.0.0
```

### 10.2 Verify Rollback

```bash
kubectl describe cronjob sqlqueryjob-pre | grep Image
```

## Troubleshooting

### Issue: CronJob Not Creating Jobs

**Check:**
```bash
kubectl describe cronjob sqlqueryjob-pre
kubectl get events --sort-by='.lastTimestamp'
```

**Common causes:**
- Incorrect schedule format
- `startingDeadlineSeconds` too short
- `concurrencyPolicy: Forbid` with long-running jobs

### Issue: Job Fails Immediately

**Check:**
```bash
kubectl get pods -l job=sqlqueryjob
kubectl logs <pod-name>
kubectl describe pod <pod-name>
```

**Common causes:**
- Image pull errors (check ACR access)
- Missing ConfigMap or Secret
- Invalid configuration
- Database connection issues

### Issue: SQL Connection Failures

**Verify:**
- Connection string format
- Firewall rules allow AKS egress
- SQL credentials are correct
- Database exists and is accessible

**Test connection:**
```bash
kubectl run -it --rm debug --image=mcr.microsoft.com/mssql-tools --restart=Never -- /bin/bash
sqlcmd -S your-server.database.windows.net -U your-user -P your-password -d YourDatabase
```

### Issue: ConfigMap Not Mounted

**Check:**
```bash
kubectl get configmap jobscheduler-config-pre
kubectl describe pod <pod-name>
```

**Verify volume mounts:**
```bash
kubectl exec <pod-name> -- ls -la /app/Configuration/PRE/
```

## Security Considerations

### Use Azure Key Vault

Instead of Kubernetes Secrets, use Azure Key Vault with CSI driver:

```bash
# Install CSI driver
helm repo add csi-secrets-store-provider-azure https://azure.github.io/secrets-store-csi-driver-provider-azure/charts
helm install csi csi-secrets-store-provider-azure/csi-secrets-store-provider-azure
```

### Enable Managed Identity

Use Azure AD Managed Identity for SQL authentication:

1. Enable managed identity on AKS
2. Grant SQL permissions to managed identity
3. Update connection string to use managed identity

### Network Policies

Implement network policies to restrict pod communication:

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: jobscheduler-netpol
spec:
  podSelector:
    matchLabels:
      app: jobscheduler
  policyTypes:
  - Egress
  egress:
  - to:
    - podSelector: {}
    ports:
    - protocol: TCP
      port: 1433  # SQL Server
```

## Backup and Disaster Recovery

### Backup Kubernetes Resources

```bash
kubectl get cronjob sqlqueryjob-pre -o yaml > backup/cronjob-pre.yaml
kubectl get configmap jobscheduler-config-pre -o yaml > backup/configmap-pre.yaml
```

### Restore from Backup

```bash
kubectl apply -f backup/cronjob-pre.yaml
kubectl apply -f backup/configmap-pre.yaml
```

## Monitoring Alerts

Set up alerts for:
- Job failures
- Long execution times
- SQL connection errors
- High resource usage

Example Prometheus alert:

```yaml
- alert: JobSchedulerFailed
  expr: kube_job_status_failed{job_name=~"sqlqueryjob.*"} > 0
  for: 5m
  labels:
    severity: critical
  annotations:
    summary: "JobScheduler job failed"
```

## Conclusion

Your JobScheduler application is now deployed and running on AKS. Monitor the logs and Elastic APM dashboard to ensure everything is working correctly.

For additional support, refer to the main README.md or contact the DevOps team.
