# Kubernetes Deployment Guide - DeltaList

This comprehensive guide covers deploying DeltaList PoC A (gRPC) and PoC B (MQTT) to Azure Kubernetes Service (AKS).

## Table of Contents

- [Prerequisites](#prerequisites)
- [Architecture Overview](#architecture-overview)
- [Environment Setup](#environment-setup)
- [Deployment Workflow](#deployment-workflow)
- [Configuration Management](#configuration-management)
- [Monitoring and Observability](#monitoring-and-observability)
- [Scaling and High Availability](#scaling-and-high-availability)
- [Security](#security)
- [Troubleshooting](#troubleshooting)
- [Maintenance and Operations](#maintenance-and-operations)

---

## Prerequisites

### Required Tools

| Tool | Version | Purpose | Installation |
|------|---------|---------|--------------|
| kubectl | ≥ 1.27 | Kubernetes CLI | [Install kubectl](https://kubernetes.io/docs/tasks/tools/) |
| Azure CLI | ≥ 2.50 | Azure management | [Install az CLI](https://docs.microsoft.com/cli/azure/install-azure-cli) |
| envsubst | Latest | Template substitution | `apt-get install gettext` |
| kubeval | ≥ 0.16 | Manifest validation | [Install kubeval](https://kubeval.instrumenta.dev/) |
| kustomize | ≥ 5.0 | Configuration management | [Install kustomize](https://kubectl.docs.kubernetes.io/installation/kustomize/) |
| helm | ≥ 3.12 | Package manager (optional) | [Install helm](https://helm.sh/docs/intro/install/) |

### Azure Prerequisites

1. **AKS Cluster**
   - Kubernetes version: ≥ 1.27
   - Node pool: Standard_D4s_v3 or better
   - Minimum nodes: 3
   - Networking: Azure CNI
   - Network Policy: Calico or Azure

2. **Azure Container Registry (ACR)**
   - SKU: Standard or Premium
   - Admin access enabled (for pull secrets)
   - Geo-replication enabled (Premium, for HA)

3. **Storage**
   - Premium_LRS storage class for Redis and EMQX
   - Backup and snapshot policies configured

4. **Network**
   - Public IP for LoadBalancers
   - Application Gateway (optional, for advanced routing)
   - Azure Firewall (optional, for egress control)

5. **Monitoring (Optional)**
   - Azure Monitor for Containers
   - Log Analytics Workspace
   - Prometheus + Grafana (recommended)

### Required Permissions

- AKS Cluster Admin
- ACR Push/Pull
- Azure Key Vault Secrets Officer (if using Key Vault)
- Network Contributor (for LoadBalancer creation)

---

## Architecture Overview

### Components

```
┌─────────────────────────────────────────────────────────────┐
│                     Azure Load Balancer                      │
└─────────────────────┬───────────────────┬───────────────────┘
                      │                   │
              ┌───────▼────────┐  ┌───────▼────────┐
              │   gRPC Backend │  │  MQTT Backend  │
              │    (PoC A)     │  │    (PoC B)     │
              │   Replicas: 3  │  │  Replicas: 3   │
              └───────┬────────┘  └───────┬────────┘
                      │                   │
                      └────────┬──────────┘
                               │
                    ┌──────────▼──────────┐
                    │       Redis         │
                    │  (Shared Cache)     │
                    └─────────────────────┘
                               │
                    ┌──────────▼──────────┐
                    │   EMQX Cluster      │
                    │  (MQTT Broker)      │
                    │   StatefulSet: 3    │
                    └─────────────────────┘
```

### Network Policies

- **Zero-trust networking**: All traffic blocked by default
- **gRPC Backend**: Accessible from LoadBalancer, can reach Redis
- **MQTT Backend**: Accessible from LoadBalancer, can reach Redis + EMQX
- **Redis**: Only accessible from backends
- **EMQX**: Accessible from external (IoT devices) + MQTT backend

---

## Environment Setup

### 1. Connect to AKS Cluster

```bash
# Login to Azure
az login

# Set subscription
az account set --subscription <subscription-id>

# Get AKS credentials
az aks get-credentials \
  --resource-group <resource-group> \
  --name <aks-cluster-name> \
  --overwrite-existing

# Verify connection
kubectl cluster-info
kubectl get nodes
```

### 2. Setup ACR Integration

```bash
# Attach ACR to AKS (recommended)
az aks update \
  --resource-group <resource-group> \
  --name <aks-cluster-name> \
  --attach-acr <acr-name>

# Or create image pull secret manually
kubectl create secret docker-registry acr-secret \
  --namespace deltalist \
  --docker-server=<acr-name>.azurecr.io \
  --docker-username=<acr-username> \
  --docker-password=<acr-password>
```

### 3. Generate Secrets

```bash
# Generate secure random secrets
export REDIS_CONNECTION_STRING="redis.deltalist.svc.cluster.local:6379"
export SECURITY_SECRET_KEY=$(openssl rand -base64 32)
export SECURITY_SALT=$(openssl rand -base64 24)
export JWT_SIGNING_KEY=$(openssl rand -base64 32)
export ENCRYPTION_KEY=$(openssl rand -base64 32)

# MQTT-specific secrets
export MQTT_USERNAME="deltalist-backend"
export MQTT_PASSWORD=$(openssl rand -base64 24)
export EMQX_NODE_COOKIE=$(openssl rand -hex 32)
export EMQX_DASHBOARD_PASSWORD=$(openssl rand -base64 24)
export EMQX_API_KEY=$(openssl rand -base64 32)
export EMQX_API_SECRET=$(openssl rand -base64 32)

# ACR and image tags
export ACR_NAME="<your-acr-name>"
export IMAGE_TAG="v1.0.0"

# Save to .env file (DO NOT COMMIT)
cat > .env.production <<EOF
ACR_NAME=${ACR_NAME}
IMAGE_TAG=${IMAGE_TAG}
REDIS_CONNECTION_STRING=${REDIS_CONNECTION_STRING}
SECURITY_SECRET_KEY=${SECURITY_SECRET_KEY}
SECURITY_SALT=${SECURITY_SALT}
JWT_SIGNING_KEY=${JWT_SIGNING_KEY}
ENCRYPTION_KEY=${ENCRYPTION_KEY}
MQTT_USERNAME=${MQTT_USERNAME}
MQTT_PASSWORD=${MQTT_PASSWORD}
EMQX_NODE_COOKIE=${EMQX_NODE_COOKIE}
EMQX_DASHBOARD_PASSWORD=${EMQX_DASHBOARD_PASSWORD}
EMQX_API_KEY=${EMQX_API_KEY}
EMQX_API_SECRET=${EMQX_API_SECRET}
EOF

# Load environment variables
source .env.production
```

### 4. (Optional) Azure Key Vault Integration

For production, use Azure Key Vault with Secrets Store CSI Driver:

```bash
# Install Secrets Store CSI Driver
helm repo add secrets-store-csi-driver https://kubernetes-sigs.github.io/secrets-store-csi-driver/charts
helm install csi-secrets-store secrets-store-csi-driver/secrets-store-csi-driver \
  --namespace kube-system

# Install Azure Key Vault Provider
kubectl apply -f https://raw.githubusercontent.com/Azure/secrets-store-csi-driver-provider-azure/master/deployment/provider-azure-installer.yaml

# Configure SecretProviderClass (see secret-template.yaml)
```

---

## Deployment Workflow

### Method 1: Automated Scripts (Recommended)

#### Deploy PoC A (gRPC Backend)

```bash
# Source environment variables
source .env.production

# Deploy gRPC backend
./scripts/deploy-grpc.sh

# Monitor deployment
kubectl get pods -n deltalist -l poc=poc-a -w
```

#### Deploy PoC B (MQTT Backend)

```bash
# Source environment variables
source .env.production

# Deploy MQTT backend
./scripts/deploy-mqtt.sh

# Monitor deployment
kubectl get pods -n deltalist -l poc=poc-b -w
```

### Method 2: Manual Deployment

```bash
# 1. Create namespace and secrets
kubectl apply -f deployment/kubernetes/grpc/namespace.yaml
envsubst < deployment/kubernetes/grpc/secret-template.yaml | kubectl apply -f -

# 2. Deploy ConfigMaps
kubectl apply -f deployment/kubernetes/grpc/configmap.yaml
kubectl apply -f deployment/kubernetes/mqtt/configmap.yaml

# 3. Deploy infrastructure
kubectl apply -f deployment/kubernetes/grpc/redis.yaml

# 4. Deploy backends
envsubst < deployment/kubernetes/grpc/deployment.yaml | kubectl apply -f -
envsubst < deployment/kubernetes/mqtt/emqx-deployment.yaml | kubectl apply -f -
envsubst < deployment/kubernetes/mqtt/mqtt-backend-deployment.yaml | kubectl apply -f -

# 5. Deploy security and HA
kubectl apply -f deployment/kubernetes/network-policy.yaml
kubectl apply -f deployment/kubernetes/pod-disruption-budget.yaml

# 6. Deploy monitoring (if Prometheus Operator installed)
kubectl apply -f deployment/kubernetes/monitoring/servicemonitor.yaml
```

### Method 3: Kustomize (Advanced)

```bash
# Deploy to development
kustomize build deployment/kubernetes/overlays/dev | kubectl apply -f -

# Deploy to production
kustomize build deployment/kubernetes/overlays/prod | kubectl apply -f -

# Preview changes without applying
kustomize build deployment/kubernetes/overlays/prod
```

---

## Configuration Management

### ConfigMaps

Located in:
- `deployment/kubernetes/grpc/configmap.yaml`
- `deployment/kubernetes/mqtt/configmap.yaml`

**Update ConfigMap:**
```bash
kubectl apply -f deployment/kubernetes/grpc/configmap.yaml

# Restart pods to pick up changes
kubectl rollout restart deployment/grpc-backend -n deltalist
```

### Secrets

**Never commit secrets to Git!** Use secret templates with environment variable substitution.

**Update secrets:**
```bash
# Update environment variables
export SECURITY_SECRET_KEY=$(openssl rand -base64 32)

# Apply updated secret
envsubst < deployment/kubernetes/grpc/secret-template.yaml | kubectl apply -f -

# Restart pods to pick up new secrets
kubectl rollout restart deployment/grpc-backend -n deltalist
```

### Environment Variables

Each deployment supports environment-specific configuration:
- `ASPNETCORE_ENVIRONMENT`: Development, Staging, Production
- `Backend__Environment`: Application environment
- Secrets via `secretKeyRef`

---

## Monitoring and Observability

### Prometheus Metrics

All backends expose metrics on port 9090 at `/metrics`:

```bash
# Port-forward to access metrics locally
kubectl port-forward -n deltalist svc/grpc-backend 9090:9090

# View metrics
curl http://localhost:9090/metrics
```

### Prometheus Operator (Recommended)

If Prometheus Operator is installed:

```bash
# Deploy ServiceMonitors
kubectl apply -f deployment/kubernetes/monitoring/servicemonitor.yaml

# Check ServiceMonitor status
kubectl get servicemonitor -n deltalist

# View Prometheus targets
kubectl port-forward -n monitoring svc/prometheus-k8s 9090:9090
# Open http://localhost:9090/targets
```

### Alerts

PrometheusRule alerts are defined in `servicemonitor.yaml`:
- High CPU/Memory usage
- Pod restart alerts
- Backend down alerts
- HPA maxed out alerts

### Logging

```bash
# View logs from all gRPC backend pods
kubectl logs -n deltalist -l app=grpc-backend --tail=100 -f

# View logs from specific pod
kubectl logs -n deltalist <pod-name> -f

# View logs from previous crashed pod
kubectl logs -n deltalist <pod-name> --previous

# Export logs to file
kubectl logs -n deltalist -l app=grpc-backend --tail=1000 > grpc-logs.txt
```

### Health Checks

```bash
# Check pod health
kubectl get pods -n deltalist

# Describe pod (includes events)
kubectl describe pod -n deltalist <pod-name>

# Execute health check manually
kubectl exec -n deltalist <pod-name> -- curl http://localhost:9090/health
```

---

## Scaling and High Availability

### Horizontal Pod Autoscaler (HPA)

HPAs are configured for both backends:

**View HPA status:**
```bash
kubectl get hpa -n deltalist

# Detailed HPA info
kubectl describe hpa grpc-backend-hpa -n deltalist
```

**Manual scaling:**
```bash
# Scale manually (overrides HPA temporarily)
kubectl scale deployment/grpc-backend -n deltalist --replicas=5

# HPA will take over after cooldown period
```

### Pod Disruption Budgets (PDB)

PDBs ensure minimum availability during voluntary disruptions:

```bash
# View PDB status
kubectl get pdb -n deltalist

# Check PDB details
kubectl describe pdb grpc-backend-pdb -n deltalist
```

### EMQX Cluster

EMQX uses StatefulSet for stable network identities:

```bash
# View EMQX cluster status
kubectl get statefulset emqx -n deltalist

# Access EMQX dashboard
kubectl port-forward -n deltalist svc/emqx 18083:18083
# Open http://localhost:18083 (admin/password from secret)

# Check cluster nodes
kubectl exec -n deltalist emqx-0 -- emqx ctl cluster status
```

---

## Security

### Network Policies

Network policies implement zero-trust networking:

```bash
# View network policies
kubectl get networkpolicy -n deltalist

# Test network connectivity
kubectl run -n deltalist --rm -it test-pod --image=nicolaka/netshoot -- bash
# Inside pod: nc -zv redis 6379
```

### RBAC

Create service accounts with minimal permissions:

```bash
# Create service account
kubectl create serviceaccount deltalist-app -n deltalist

# Bind role
kubectl create rolebinding deltalist-app-binding \
  --clusterrole=view \
  --serviceaccount=deltalist:deltalist-app \
  -n deltalist
```

### Secrets Management

**Production best practices:**
1. Use Azure Key Vault with Secrets Store CSI Driver
2. Enable encryption at rest for etcd
3. Use RBAC to restrict secret access
4. Rotate secrets regularly
5. Never log secrets

### Security Context

Production deployments include:
- `runAsNonRoot: true`
- `allowPrivilegeEscalation: false`
- `readOnlyRootFilesystem: true` (where possible)
- Dropped capabilities

---

## Troubleshooting

### Common Issues

#### Pods not starting

```bash
# Check pod events
kubectl describe pod -n deltalist <pod-name>

# Common causes:
# - ImagePullBackOff: Check ACR credentials
# - CrashLoopBackOff: Check logs for application errors
# - Pending: Check resource availability
```

#### ImagePullBackOff

```bash
# Verify ACR integration
az aks check-acr --resource-group <rg> --name <aks> --acr <acr-name>

# Check image pull secret
kubectl get secret acr-secret -n deltalist -o yaml

# Manually pull image to verify
kubectl run -n deltalist test --image=<acr-name>.azurecr.io/grpc-backend:latest --rm -it -- /bin/sh
```

#### Service not accessible

```bash
# Check service endpoints
kubectl get svc -n deltalist
kubectl get endpoints -n deltalist

# Check LoadBalancer status
kubectl describe svc grpc-backend -n deltalist

# Test from within cluster
kubectl run -n deltalist test-curl --image=curlimages/curl --rm -it -- curl http://grpc-backend:9090/health
```

#### Redis connection issues

```bash
# Check Redis pod
kubectl get pod -n deltalist -l app=redis

# Test Redis connectivity
kubectl exec -n deltalist <backend-pod> -- sh -c "nc -zv redis 6379"

# Access Redis CLI
kubectl exec -n deltalist -it <redis-pod> -- redis-cli
# > PING
# PONG
```

#### EMQX cluster not forming

```bash
# Check StatefulSet status
kubectl get statefulset emqx -n deltalist

# Check EMQX logs
kubectl logs -n deltalist emqx-0 --tail=100

# Verify headless service
kubectl get svc emqx-headless -n deltalist

# Check cluster status
kubectl exec -n deltalist emqx-0 -- emqx ctl cluster status
```

### Debug Commands

```bash
# Get all resources in namespace
kubectl get all -n deltalist

# Describe all pods (see events)
kubectl describe pods -n deltalist

# View resource usage
kubectl top nodes
kubectl top pods -n deltalist

# Check HPA metrics
kubectl get hpa -n deltalist -w

# View persistent volumes
kubectl get pv,pvc -n deltalist
```

---

## Maintenance and Operations

### Updating Deployments

#### Rolling update (zero downtime)

```bash
# Update image tag
export IMAGE_TAG="v1.1.0"

# Apply update
envsubst < deployment/kubernetes/grpc/deployment.yaml | kubectl apply -f -

# Watch rollout
kubectl rollout status deployment/grpc-backend -n deltalist

# Check rollout history
kubectl rollout history deployment/grpc-backend -n deltalist
```

#### Rollback

```bash
# Rollback to previous version
./scripts/rollback.sh --deployment grpc-backend

# Rollback to specific revision
./scripts/rollback.sh --deployment grpc-backend --revision 3

# Rollback all PoC A
./scripts/rollback.sh --all-poc-a
```

### Backup and Restore

#### Redis backup

```bash
# Trigger Redis save
kubectl exec -n deltalist <redis-pod> -- redis-cli SAVE

# Copy RDB file
kubectl cp deltalist/<redis-pod>:/data/dump.rdb ./redis-backup.rdb

# Restore
kubectl cp ./redis-backup.rdb deltalist/<redis-pod>:/data/dump.rdb
kubectl exec -n deltalist <redis-pod> -- redis-cli SHUTDOWN NOSAVE
# Pod will restart and load backup
```

#### EMQX backup

```bash
# Export EMQX data
kubectl exec -n deltalist emqx-0 -- emqx ctl data export

# Copy to local
kubectl cp deltalist/emqx-0:/opt/emqx/data/export ./emqx-backup/

# Restore
kubectl cp ./emqx-backup/ deltalist/emqx-0:/opt/emqx/data/import/
kubectl exec -n deltalist emqx-0 -- emqx ctl data import
```

### Cluster Upgrades

```bash
# Upgrade AKS cluster
az aks upgrade \
  --resource-group <resource-group> \
  --name <aks-cluster-name> \
  --kubernetes-version 1.28.0

# Monitor upgrade
kubectl get nodes -w
```

### Cost Optimization

```bash
# Use dev overlays for non-production
kustomize build deployment/kubernetes/overlays/dev | kubectl apply -f -

# Scale down non-production environments
kubectl scale deployment --all --replicas=1 -n deltalist-dev

# Use spot instances for dev/test
az aks nodepool add \
  --resource-group <rg> \
  --cluster-name <aks> \
  --name spotpool \
  --priority Spot \
  --eviction-policy Delete \
  --spot-max-price -1 \
  --node-count 2
```

---

## Best Practices

### Pre-deployment Checklist

- [ ] All manifests validated with `kubeval`
- [ ] Secrets generated and stored securely
- [ ] ACR images built and pushed
- [ ] Resource quotas configured
- [ ] Network policies tested
- [ ] Monitoring configured
- [ ] Backup strategy defined
- [ ] Rollback plan documented

### Production Readiness

- [ ] Multi-region deployment (geo-redundancy)
- [ ] Azure Application Gateway + WAF
- [ ] Azure Front Door (global load balancing)
- [ ] Pod Security Policies/Standards
- [ ] Resource quotas and limits
- [ ] Audit logging enabled
- [ ] Disaster recovery plan
- [ ] SLA monitoring and alerting

### Regular Maintenance

- [ ] Weekly: Review metrics and alerts
- [ ] Monthly: Security patches and updates
- [ ] Quarterly: Disaster recovery drills
- [ ] Yearly: Architecture review

---

## Additional Resources

### Documentation
- [Kubernetes Official Docs](https://kubernetes.io/docs/)
- [AKS Documentation](https://docs.microsoft.com/azure/aks/)
- [Prometheus Operator](https://prometheus-operator.dev/)
- [EMQX Documentation](https://www.emqx.io/docs/en/v5.0/)

### Tools
- [kubectl Cheat Sheet](https://kubernetes.io/docs/reference/kubectl/cheatsheet/)
- [Kustomize](https://kustomize.io/)
- [Kubeval](https://kubeval.instrumenta.dev/)
- [kube-score](https://kube-score.com/)

### DeltaList Specific
- [Architecture Documentation](./ARCHITECTURE.md)
- [PoC Comparison](./POC_COMPARISON.md)
- [GitHub Repository](https://github.com/deltalist)

---

## Support

For issues or questions:
- Create GitHub issue
- Contact DevOps team
- Email: devops@deltalist.com

---

**Last Updated:** 2025-11-17
**Version:** 1.0.0
**Maintained by:** DeltaList DevOps Team
