# Kubernetes Manifests - DeltaList

This directory contains production-ready Kubernetes manifests for deploying DeltaList PoC A (gRPC) and PoC B (MQTT) to Azure Kubernetes Service (AKS).

## Directory Structure

```
kubernetes/
├── base/                           # Kustomize base configuration
│   └── kustomization.yaml         # Base kustomization
├── overlays/                       # Environment-specific overlays
│   ├── dev/                       # Development environment
│   │   ├── kustomization.yaml
│   │   └── patches/
│   │       ├── dev-hpa.yaml
│   │       └── dev-resources.yaml
│   └── prod/                      # Production environment
│       ├── kustomization.yaml
│       └── patches/
│           ├── prod-hpa.yaml
│           ├── prod-resources.yaml
│           └── prod-security.yaml
├── grpc/                          # gRPC Backend (PoC A)
│   ├── namespace.yaml             # Namespace and base secrets
│   ├── configmap.yaml             # Application configuration
│   ├── secret-template.yaml       # Secret template (DO NOT COMMIT REAL VALUES)
│   ├── deployment.yaml            # Deployment, Service, HPA
│   └── redis.yaml                 # Redis cache
├── mqtt/                          # MQTT Backend (PoC B)
│   ├── configmap.yaml             # Application configuration
│   ├── secret-template.yaml       # Secret template (DO NOT COMMIT REAL VALUES)
│   ├── mqtt-backend-deployment.yaml  # MQTT backend deployment
│   ├── emqx-deployment.yaml       # EMQX MQTT broker (StatefulSet)
│   └── values.yaml                # Helm values (if using Helm)
├── monitoring/                    # Monitoring resources
│   ├── prometheus-config.yaml     # Prometheus scrape config
│   └── servicemonitor.yaml        # ServiceMonitor for Prometheus Operator
├── network-policy.yaml            # Network policies (zero-trust)
├── pod-disruption-budget.yaml     # PDBs for high availability
└── README.md                      # This file
```

## Quick Start

### Deploy PoC A (gRPC Backend)

```bash
# Set environment variables
export ACR_NAME="your-acr-name"
export IMAGE_TAG="v1.0.0"
export REDIS_CONNECTION_STRING="redis:6379"
export SECURITY_SECRET_KEY=$(openssl rand -base64 32)
export SECURITY_SALT=$(openssl rand -base64 24)
export JWT_SIGNING_KEY=$(openssl rand -base64 32)
export ENCRYPTION_KEY=$(openssl rand -base64 32)

# Run deployment script
./scripts/deploy-grpc.sh
```

### Deploy PoC B (MQTT Backend)

```bash
# Set additional MQTT environment variables
export MQTT_USERNAME="deltalist-backend"
export MQTT_PASSWORD=$(openssl rand -base64 24)
export EMQX_NODE_COOKIE=$(openssl rand -hex 32)
export EMQX_DASHBOARD_PASSWORD=$(openssl rand -base64 24)

# Run deployment script
./scripts/deploy-mqtt.sh
```

## Deployment Methods

### Method 1: Automated Scripts (Recommended)

Use the provided deployment scripts in `scripts/`:
- `deploy-grpc.sh` - Deploy gRPC backend
- `deploy-mqtt.sh` - Deploy MQTT backend
- `rollback.sh` - Rollback deployments
- `validate-manifests.sh` - Validate manifests before deployment

### Method 2: Manual kubectl

```bash
# 1. Create namespace
kubectl apply -f grpc/namespace.yaml

# 2. Create secrets
envsubst < grpc/secret-template.yaml | kubectl apply -f -

# 3. Deploy resources
kubectl apply -f grpc/configmap.yaml
kubectl apply -f grpc/redis.yaml
envsubst < grpc/deployment.yaml | kubectl apply -f -

# 4. Deploy security
kubectl apply -f network-policy.yaml
kubectl apply -f pod-disruption-budget.yaml
```

### Method 3: Kustomize

```bash
# Deploy to development
kustomize build overlays/dev | kubectl apply -f -

# Deploy to production
kustomize build overlays/prod | kubectl apply -f -
```

## Components

### PoC A - gRPC Backend

| Component | Type | Replicas | Resources |
|-----------|------|----------|-----------|
| gRPC Backend | Deployment | 3-20 (HPA) | 500m-2000m CPU, 512Mi-2Gi RAM |
| Redis | Deployment | 1 | 250m-1000m CPU, 512Mi-2Gi RAM |

**Features:**
- Horizontal Pod Autoscaler (CPU/Memory based)
- Health and readiness probes
- Prometheus metrics on port 9090
- Network policies for security
- Pod Disruption Budget (min 2 available)

### PoC B - MQTT Backend

| Component | Type | Replicas | Resources |
|-----------|------|----------|-----------|
| MQTT Backend | Deployment | 3-15 (HPA) | 500m-2000m CPU, 512Mi-2Gi RAM |
| EMQX Broker | StatefulSet | 3 | 1000m-4000m CPU, 2Gi-8Gi RAM |
| Redis | Deployment | 1 (shared) | 250m-1000m CPU, 512Mi-2Gi RAM |

**Features:**
- EMQX cluster with 3 nodes
- WebSocket support (ports 8083/8084)
- MQTT/MQTTS (ports 1883/8883)
- Dashboard on port 18083
- Persistent storage for EMQX

## Configuration

### ConfigMaps

Application configuration is stored in ConfigMaps:
- `grpc/configmap.yaml` - gRPC backend settings
- `mqtt/configmap.yaml` - MQTT backend settings

**Update ConfigMap:**
```bash
kubectl apply -f grpc/configmap.yaml
kubectl rollout restart deployment/grpc-backend -n deltalist
```

### Secrets

**IMPORTANT:** Never commit secrets to Git!

Use secret templates with environment variable substitution:
```bash
envsubst < grpc/secret-template.yaml | kubectl apply -f -
```

For production, consider:
- Azure Key Vault with Secrets Store CSI Driver
- External Secrets Operator
- Sealed Secrets

## Security

### Network Policies

Zero-trust networking is enforced via NetworkPolicy:
- Default deny all ingress/egress
- Explicit allow rules for each component
- DNS resolution allowed
- External HTTPS allowed (for integrations)

### Pod Security

Production overlays include:
- `runAsNonRoot: true`
- `allowPrivilegeEscalation: false`
- `readOnlyRootFilesystem: true` (where possible)
- Dropped capabilities
- Seccomp profiles

## Monitoring

### Prometheus Metrics

All backends expose metrics:
- **Endpoint:** `/metrics`
- **Port:** 9090
- **Format:** Prometheus

### Prometheus Operator

If Prometheus Operator is installed:
```bash
kubectl apply -f monitoring/servicemonitor.yaml
```

ServiceMonitors automatically configure scraping.

### Alerts

PrometheusRule includes alerts for:
- High CPU/Memory usage
- Pod restarts
- Backend down
- HPA at max capacity

## High Availability

### Horizontal Pod Autoscaler

HPAs are configured for both backends:
- **Metrics:** CPU (70%), Memory (80%)
- **Scale down:** Stabilization 5min, max 50% reduction/min
- **Scale up:** No stabilization, max 100% increase/30s

### Pod Disruption Budgets

PDBs ensure availability during disruptions:
- gRPC Backend: min 2 available
- MQTT Backend: min 2 available
- EMQX: min 2 available
- Redis: min 1 available

## Validation

Validate manifests before deployment:

```bash
# YAML syntax check
./scripts/check-yaml-syntax.sh

# Full validation (requires kubectl)
./scripts/validate-manifests.sh

# Dry-run deployment
kubectl apply --dry-run=server -f grpc/deployment.yaml
```

## Troubleshooting

### Common Issues

**Pods not starting:**
```bash
kubectl describe pod -n deltalist <pod-name>
kubectl logs -n deltalist <pod-name>
```

**ImagePullBackOff:**
```bash
# Check ACR integration
az aks check-acr --resource-group <rg> --name <aks> --acr <acr-name>
```

**Service not accessible:**
```bash
kubectl get svc -n deltalist
kubectl get endpoints -n deltalist
```

### Useful Commands

```bash
# View all resources
kubectl get all -n deltalist

# View events
kubectl get events -n deltalist --sort-by='.lastTimestamp'

# Check HPA status
kubectl get hpa -n deltalist

# View logs
kubectl logs -n deltalist -l app=grpc-backend --tail=100 -f

# Execute into pod
kubectl exec -n deltalist -it <pod-name> -- /bin/sh
```

## Rollback

Rollback to previous version:

```bash
# Rollback specific deployment
./scripts/rollback.sh --deployment grpc-backend

# Rollback to specific revision
./scripts/rollback.sh --deployment grpc-backend --revision 3

# Rollback all PoC A
./scripts/rollback.sh --all-poc-a
```

## Maintenance

### Updating Images

```bash
# Update image tag
export IMAGE_TAG="v1.1.0"

# Apply update
envsubst < grpc/deployment.yaml | kubectl apply -f -

# Watch rollout
kubectl rollout status deployment/grpc-backend -n deltalist
```

### Scaling

```bash
# Manual scale
kubectl scale deployment/grpc-backend -n deltalist --replicas=5

# Update HPA
kubectl edit hpa grpc-backend-hpa -n deltalist
```

## Best Practices

1. **Always validate before deployment**
   ```bash
   ./scripts/validate-manifests.sh
   ```

2. **Use dry-run first**
   ```bash
   kubectl apply --dry-run=server -f deployment.yaml
   ```

3. **Deploy to dev first**
   ```bash
   kustomize build overlays/dev | kubectl apply -f -
   ```

4. **Monitor rollout**
   ```bash
   kubectl rollout status deployment/grpc-backend -n deltalist
   ```

5. **Keep secrets out of Git**
   - Use templates with environment variables
   - Use Azure Key Vault in production
   - Never commit `.env` files

6. **Regular backups**
   - Redis data
   - EMQX configuration
   - Persistent volumes

## Resources

- [Full Deployment Guide](../../docs/KUBERNETES_DEPLOYMENT.md)
- [Architecture Documentation](../../docs/ARCHITECTURE.md)
- [PoC Comparison](../../docs/POC_COMPARISON.md)

## Support

For issues or questions:
- Create GitHub issue
- Contact DevOps team
- Email: devops@deltalist.com

---

**Last Updated:** 2025-11-17
**Maintained by:** DeltaList DevOps Team
