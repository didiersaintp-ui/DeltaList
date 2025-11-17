#!/bin/bash
#############################################################################
# deploy-mqtt.sh - Deploy MQTT Backend (PoC B) to AKS
#############################################################################
set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
K8S_DIR="${PROJECT_ROOT}/deployment/kubernetes"

#############################################################################
# Functions
#############################################################################

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

check_prerequisites() {
    log_info "Checking prerequisites..."

    # Check kubectl
    if ! command -v kubectl &> /dev/null; then
        log_error "kubectl not found. Please install kubectl."
        exit 1
    fi

    # Check envsubst
    if ! command -v envsubst &> /dev/null; then
        log_error "envsubst not found. Please install gettext package."
        exit 1
    fi

    # Check cluster connection
    if ! kubectl cluster-info &> /dev/null; then
        log_error "Cannot connect to Kubernetes cluster. Please configure kubectl."
        exit 1
    fi

    log_info "Prerequisites check passed"
}

check_required_env_vars() {
    log_info "Checking required environment variables..."

    local required_vars=(
        "ACR_NAME"
        "IMAGE_TAG"
        "REDIS_CONNECTION_STRING"
        "SECURITY_SECRET_KEY"
        "SECURITY_SALT"
        "JWT_SIGNING_KEY"
        "ENCRYPTION_KEY"
        "MQTT_USERNAME"
        "MQTT_PASSWORD"
        "EMQX_NODE_COOKIE"
        "EMQX_DASHBOARD_PASSWORD"
    )

    local missing_vars=()

    for var in "${required_vars[@]}"; do
        if [[ -z "${!var:-}" ]]; then
            missing_vars+=("$var")
        fi
    done

    if [[ ${#missing_vars[@]} -gt 0 ]]; then
        log_error "Missing required environment variables:"
        for var in "${missing_vars[@]}"; do
            echo "  - $var"
        done
        echo ""
        echo "Please set these variables or source an environment file:"
        echo "  export ACR_NAME=your-acr-name"
        echo "  export IMAGE_TAG=latest"
        echo "  export REDIS_CONNECTION_STRING='redis:6379'"
        echo "  export SECURITY_SECRET_KEY=\$(openssl rand -base64 32)"
        echo "  export SECURITY_SALT=\$(openssl rand -base64 24)"
        echo "  export JWT_SIGNING_KEY=\$(openssl rand -base64 32)"
        echo "  export ENCRYPTION_KEY=\$(openssl rand -base64 32)"
        echo "  export MQTT_USERNAME=deltalist-backend"
        echo "  export MQTT_PASSWORD=\$(openssl rand -base64 24)"
        echo "  export EMQX_NODE_COOKIE=\$(openssl rand -hex 32)"
        echo "  export EMQX_DASHBOARD_PASSWORD=\$(openssl rand -base64 24)"
        echo "  export EMQX_API_KEY=\$(openssl rand -base64 32)"
        echo "  export EMQX_API_SECRET=\$(openssl rand -base64 32)"
        exit 1
    fi

    log_info "All required environment variables are set"
}

deploy_namespace() {
    log_info "Creating namespace..."
    kubectl apply -f "${K8S_DIR}/grpc/namespace.yaml"
}

deploy_secrets() {
    log_info "Deploying secrets..."

    # Process secret template with environment variables
    envsubst < "${K8S_DIR}/mqtt/secret-template.yaml" | kubectl apply -f -

    log_info "Secrets deployed successfully"
}

deploy_configmap() {
    log_info "Deploying ConfigMap..."
    kubectl apply -f "${K8S_DIR}/mqtt/configmap.yaml"
}

deploy_redis() {
    log_info "Deploying Redis (if not already deployed)..."
    kubectl apply -f "${K8S_DIR}/grpc/redis.yaml"

    # Wait for Redis to be ready
    log_info "Waiting for Redis to be ready..."
    kubectl wait --for=condition=ready pod -l app=redis -n deltalist --timeout=300s || true
}

deploy_emqx() {
    log_info "Deploying EMQX MQTT Broker..."

    # Process EMQX deployment with environment variables
    envsubst < "${K8S_DIR}/mqtt/emqx-deployment.yaml" | kubectl apply -f -

    log_info "Waiting for EMQX cluster to form..."
    sleep 10

    # Wait for at least one EMQX pod to be ready
    kubectl wait --for=condition=ready pod -l app=emqx -n deltalist --timeout=300s || true
}

deploy_backend() {
    log_info "Deploying MQTT Backend..."

    # Substitute environment variables in deployment
    envsubst < "${K8S_DIR}/mqtt/mqtt-backend-deployment.yaml" | kubectl apply -f -

    log_info "MQTT Backend deployment created"
}

deploy_network_policy() {
    log_info "Deploying Network Policies..."
    kubectl apply -f "${K8S_DIR}/network-policy.yaml"
}

deploy_pdb() {
    log_info "Deploying Pod Disruption Budgets..."
    kubectl apply -f "${K8S_DIR}/pod-disruption-budget.yaml"
}

deploy_monitoring() {
    log_info "Deploying monitoring resources..."

    # Check if Prometheus Operator is installed
    if kubectl get crd servicemonitors.monitoring.coreos.com &> /dev/null; then
        kubectl apply -f "${K8S_DIR}/monitoring/servicemonitor.yaml"
        log_info "ServiceMonitors deployed"
    else
        log_warn "Prometheus Operator not found. Skipping ServiceMonitor deployment."
        log_warn "Prometheus can still scrape using pod annotations."
    fi
}

wait_for_deployment() {
    log_info "Waiting for MQTT Backend deployment to be ready..."

    kubectl rollout status deployment/mqtt-backend -n deltalist --timeout=600s

    log_info "Deployment is ready!"
}

show_status() {
    log_info "Deployment status:"
    echo ""

    kubectl get all -n deltalist -l poc=poc-b
    echo ""

    log_info "Service endpoints:"
    kubectl get svc mqtt-backend -n deltalist
    kubectl get svc emqx -n deltalist
    echo ""

    # Get LoadBalancer IPs if available
    local mqtt_lb_ip=$(kubectl get svc mqtt-backend -n deltalist -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "pending")
    local emqx_lb_ip=$(kubectl get svc emqx -n deltalist -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "pending")

    if [[ "$mqtt_lb_ip" != "pending" ]]; then
        log_info "MQTT Backend HTTP API is accessible at: http://${mqtt_lb_ip}"
    else
        log_warn "MQTT Backend LoadBalancer IP is still pending."
    fi

    if [[ "$emqx_lb_ip" != "pending" ]]; then
        log_info "EMQX MQTT Broker is accessible at: ${emqx_lb_ip}:1883"
        log_info "EMQX Dashboard is accessible at: http://${emqx_lb_ip}:18083"
    else
        log_warn "EMQX LoadBalancer IP is still pending."
    fi
}

#############################################################################
# Main
#############################################################################

main() {
    log_info "=========================================="
    log_info "Deploying MQTT Backend (PoC B) to AKS"
    log_info "=========================================="
    echo ""

    check_prerequisites
    check_required_env_vars

    # Deploy resources in order
    deploy_namespace
    deploy_secrets
    deploy_configmap
    deploy_redis
    deploy_emqx
    deploy_backend
    deploy_network_policy
    deploy_pdb
    deploy_monitoring

    wait_for_deployment
    show_status

    echo ""
    log_info "=========================================="
    log_info "MQTT Backend deployment completed!"
    log_info "=========================================="
    echo ""
    echo "Next steps:"
    echo "  1. Verify health: kubectl get pods -n deltalist -l app=mqtt-backend"
    echo "  2. Check logs: kubectl logs -n deltalist -l app=mqtt-backend --tail=50"
    echo "  3. Access EMQX dashboard: http://<EMQX-IP>:18083 (admin/password)"
    echo "  4. Test MQTT: mosquitto_pub -h <EMQX-IP> -p 1883 -t test -m 'hello'"
    echo "  5. View metrics: kubectl port-forward -n deltalist svc/mqtt-backend 9090:9090"
    echo ""
}

# Run main function
main "$@"
