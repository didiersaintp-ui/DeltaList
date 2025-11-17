# Quick Start - DeltaList Kubernetes Deployment

Fast-track guide to deploy DeltaList to Azure Kubernetes Service (AKS).

## Prerequisites Checklist

- [ ] AKS cluster running (Kubernetes ≥ 1.27)
- [ ] kubectl configured and connected to AKS
- [ ] Azure Container Registry (ACR) with images pushed
- [ ] `envsubst` installed (`apt-get install gettext`)

## Step 1: Connect to AKS

```bash
# Login to Azure
az login

# Get AKS credentials
az aks get-credentials \
  --resource-group <your-rg> \
  --name <your-aks-cluster> \
  --overwrite-existing

# Verify connection
kubectl cluster-info
kubectl get nodes
```

## Step 2: Setup ACR Integration

```bash
# Attach ACR to AKS (recommended method)
az aks update \
  --resource-group <your-rg> \
  --name <your-aks-cluster> \
  --attach-acr <your-acr-name>

# Verify
az aks check-acr \
  --resource-group <your-rg> \
  --name <your-aks-cluster> \
  --acr <your-acr-name>
```

## Step 3: Generate Secrets

```bash
# Generate all required secrets
export ACR_NAME="your-acr-name"
export IMAGE_TAG="v1.0.0"
export REDIS_CONNECTION_STRING="redis.deltalist.svc.cluster.local:6379"
export SECURITY_SECRET_KEY=$(openssl rand -base64 32)
export SECURITY_SALT=$(openssl rand -base64 24)
export JWT_SIGNING_KEY=$(openssl rand -base64 32)
export ENCRYPTION_KEY=$(openssl rand -base64 32)
export MQTT_USERNAME="deltalist-backend"
export MQTT_PASSWORD=$(openssl rand -base64 24)
export EMQX_NODE_COOKIE=$(openssl rand -hex 32)
export EMQX_DASHBOARD_PASSWORD=$(openssl rand -base64 24)
export EMQX_API_KEY=$(openssl rand -base64 32)
export EMQX_API_SECRET=$(openssl rand -base64 32)

# Save to .env.production (NEVER commit this file!)
cat > .env.production <<'EOF'
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

# Substitute variables
envsubst < .env.production > .env.production.tmp && mv .env.production.tmp .env.production

# Load environment
source .env.production
```

## Step 4: Deploy PoC A (gRPC Backend)

```bash
# Navigate to project root
cd /path/to/DeltaList

# Deploy using script
./scripts/deploy-grpc.sh

# Or manual deployment
kubectl apply -f deployment/kubernetes/grpc/namespace.yaml
envsubst < deployment/kubernetes/grpc/secret-template.yaml | kubectl apply -f -
kubectl apply -f deployment/kubernetes/grpc/configmap.yaml
kubectl apply -f deployment/kubernetes/grpc/redis.yaml
envsubst < deployment/kubernetes/grpc/deployment.yaml | kubectl apply -f -
kubectl apply -f deployment/kubernetes/network-policy.yaml
kubectl apply -f deployment/kubernetes/pod-disruption-budget.yaml

# Wait for deployment
kubectl rollout status deployment/grpc-backend -n deltalist
```

## Step 5: Deploy PoC B (MQTT Backend)

```bash
# Deploy using script
./scripts/deploy-mqtt.sh

# Or manual deployment
envsubst < deployment/kubernetes/mqtt/secret-template.yaml | kubectl apply -f -
kubectl apply -f deployment/kubernetes/mqtt/configmap.yaml
envsubst < deployment/kubernetes/mqtt/emqx-deployment.yaml | kubectl apply -f -
envsubst < deployment/kubernetes/mqtt/mqtt-backend-deployment.yaml | kubectl apply -f -

# Wait for deployment
kubectl rollout status deployment/mqtt-backend -n deltalist
kubectl rollout status statefulset/emqx -n deltalist
```

## Step 6: Verify Deployment

```bash
# Check all pods
kubectl get pods -n deltalist

# Check services and external IPs
kubectl get svc -n deltalist

# Check HPA status
kubectl get hpa -n deltalist

# View logs
kubectl logs -n deltalist -l app=grpc-backend --tail=50
kubectl logs -n deltalist -l app=mqtt-backend --tail=50
```

## Step 7: Test Endpoints

```bash
# Get LoadBalancer IPs
export GRPC_IP=$(kubectl get svc grpc-backend -n deltalist -o jsonpath='{.status.loadBalancer.ingress[0].ip}')
export MQTT_API_IP=$(kubectl get svc mqtt-backend -n deltalist -o jsonpath='{.status.loadBalancer.ingress[0].ip}')
export EMQX_IP=$(kubectl get svc emqx -n deltalist -o jsonpath='{.status.loadBalancer.ingress[0].ip}')

# Test gRPC health
grpcurl -plaintext ${GRPC_IP}:5001 grpc.health.v1.Health/Check

# Test MQTT API health
curl http://${MQTT_API_IP}/health

# Access EMQX dashboard
echo "EMQX Dashboard: http://${EMQX_IP}:18083"
echo "Username: admin"
echo "Password: ${EMQX_DASHBOARD_PASSWORD}"

# Test Prometheus metrics
curl http://${GRPC_IP}:9090/metrics
curl http://${MQTT_API_IP}:9090/metrics
```

## Troubleshooting

### Pods not starting

```bash
# Describe pod to see events
kubectl describe pod -n deltalist <pod-name>

# Check logs
kubectl logs -n deltalist <pod-name>

# Check previous logs if crashed
kubectl logs -n deltalist <pod-name> --previous
```

### ImagePullBackOff

```bash
# Verify ACR integration
az aks check-acr --resource-group <rg> --name <aks> --acr <acr>

# Check if image exists
az acr repository show-tags --name <acr> --repository grpc-backend
az acr repository show-tags --name <acr> --repository mqtt-backend
```

### Service has no external IP

```bash
# Check service
kubectl get svc -n deltalist -w

# Describe service
kubectl describe svc grpc-backend -n deltalist

# Check LoadBalancer events
kubectl get events -n deltalist --field-selector involvedObject.name=grpc-backend
```

### EMQX cluster not forming

```bash
# Check StatefulSet
kubectl get statefulset emqx -n deltalist

# Check logs
kubectl logs -n deltalist emqx-0 --tail=100

# Check cluster status
kubectl exec -n deltalist emqx-0 -- emqx ctl cluster status

# Restart if needed
kubectl rollout restart statefulset/emqx -n deltalist
```

## Monitoring

```bash
# Port-forward Prometheus metrics
kubectl port-forward -n deltalist svc/grpc-backend 9090:9090

# View metrics
curl http://localhost:9090/metrics

# Deploy Prometheus Operator ServiceMonitors (if installed)
kubectl apply -f deployment/kubernetes/monitoring/servicemonitor.yaml

# Check ServiceMonitor
kubectl get servicemonitor -n deltalist
```

## Scaling

```bash
# Manual scale
kubectl scale deployment/grpc-backend -n deltalist --replicas=5

# View HPA status
kubectl get hpa -n deltalist -w

# Update HPA
kubectl edit hpa grpc-backend-hpa -n deltalist
```

## Rollback

```bash
# Rollback to previous version
./scripts/rollback.sh --deployment grpc-backend

# Rollback to specific revision
kubectl rollout history deployment/grpc-backend -n deltalist
./scripts/rollback.sh --deployment grpc-backend --revision 2

# Rollback all PoC A
./scripts/rollback.sh --all-poc-a
```

## Clean Up

```bash
# Delete all resources
kubectl delete namespace deltalist

# Or delete specific components
kubectl delete -f deployment/kubernetes/grpc/
kubectl delete -f deployment/kubernetes/mqtt/
```

## Using Kustomize (Alternative)

```bash
# Deploy to production with Kustomize
kustomize build deployment/kubernetes/overlays/prod | kubectl apply -f -

# Deploy to development
kustomize build deployment/kubernetes/overlays/dev | kubectl apply -f -

# Preview without applying
kustomize build deployment/kubernetes/overlays/prod
```

## Next Steps

1. ✅ Verify all pods are Running
2. ✅ Check HPA is working
3. ✅ Test external endpoints
4. ✅ Review logs for errors
5. ✅ Run load tests
6. ✅ Setup monitoring dashboards
7. ✅ Configure alerts
8. ✅ Document operational procedures

## Resources

- [Full Deployment Guide](../../docs/KUBERNETES_DEPLOYMENT.md)
- [Kubernetes Manifests README](./README.md)
- [Architecture Documentation](../../docs/ARCHITECTURE.md)
- [Validation Report](../../VALIDATION_REPORT.md)

## Support

For issues:
- Check logs: `kubectl logs -n deltalist <pod-name>`
- View events: `kubectl get events -n deltalist`
- Contact: devops@deltalist.com

---

**Happy Deploying! 🚀**
