#!/bin/bash
#############################################################################
# deploy-grpc.sh - Deploy gRPC Backend (PoC A) to AKS
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
        exit 1
    fi

    log_info "All required environment variables are set"
}

deploy_namespace() {
    log_info "Creating namespace and base secrets..."
    kubectl apply -f "${K8S_DIR}/grpc/namespace.yaml"
}

deploy_secrets() {
    log_info "Deploying secrets..."

    # Process secret template with environment variables
    envsubst < "${K8S_DIR}/grpc/secret-template.yaml" | kubectl apply -f -

    log_info "Secrets deployed successfully"
}

deploy_configmap() {
    log_info "Deploying ConfigMap..."
    kubectl apply -f "${K8S_DIR}/grpc/configmap.yaml"
}

deploy_redis() {
    log_info "Deploying Redis..."
    kubectl apply -f "${K8S_DIR}/grpc/redis.yaml"

    # Wait for Redis to be ready
    log_info "Waiting for Redis to be ready..."
    kubectl wait --for=condition=ready pod -l app=redis -n deltalist --timeout=300s || true
}

deploy_backend() {
    log_info "Deploying gRPC Backend..."

    # Substitute environment variables in deployment
    envsubst < "${K8S_DIR}/grpc/deployment.yaml" | kubectl apply -f -

    log_info "gRPC Backend deployment created"
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
    log_info "Waiting for gRPC Backend deployment to be ready..."

    kubectl rollout status deployment/grpc-backend -n deltalist --timeout=600s

    log_info "Deployment is ready!"
}

show_status() {
    log_info "Deployment status:"
    echo ""

    kubectl get all -n deltalist -l poc=poc-a
    echo ""

    log_info "Service endpoints:"
    kubectl get svc grpc-backend -n deltalist
    echo ""

    # Get LoadBalancer IP if available
    local lb_ip=$(kubectl get svc grpc-backend -n deltalist -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "pending")
    if [[ "$lb_ip" != "pending" ]]; then
        log_info "gRPC Backend is accessible at: ${lb_ip}:5001"
    else
        log_warn "LoadBalancer IP is still pending. Check with: kubectl get svc grpc-backend -n deltalist"
    fi
}

#############################################################################
# Main
#############################################################################

main() {
    log_info "=========================================="
    log_info "Deploying gRPC Backend (PoC A) to AKS"
    log_info "=========================================="
    echo ""

    check_prerequisites
    check_required_env_vars

    # Deploy resources in order
    deploy_namespace
    deploy_secrets
    deploy_configmap
    deploy_redis
    deploy_backend
    deploy_network_policy
    deploy_pdb
    deploy_monitoring

    wait_for_deployment
    show_status

    echo ""
    log_info "=========================================="
    log_info "gRPC Backend deployment completed!"
    log_info "=========================================="
    echo ""
    echo "Next steps:"
    echo "  1. Verify health: kubectl get pods -n deltalist -l app=grpc-backend"
    echo "  2. Check logs: kubectl logs -n deltalist -l app=grpc-backend --tail=50"
    echo "  3. Test endpoint: grpcurl -plaintext <EXTERNAL-IP>:5001 list"
    echo "  4. View metrics: kubectl port-forward -n deltalist svc/grpc-backend 9090:9090"
    echo ""
}

# Run main function
main "$@"
