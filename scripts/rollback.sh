#!/bin/bash
#############################################################################
# rollback.sh - Rollback Kubernetes deployments to previous version
#############################################################################
set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Constants
NAMESPACE="deltalist"

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

log_debug() {
    echo -e "${BLUE}[DEBUG]${NC} $1"
}

check_prerequisites() {
    if ! command -v kubectl &> /dev/null; then
        log_error "kubectl not found. Please install kubectl."
        exit 1
    fi

    if ! kubectl cluster-info &> /dev/null; then
        log_error "Cannot connect to Kubernetes cluster."
        exit 1
    fi
}

list_deployments() {
    log_info "Available deployments in namespace ${NAMESPACE}:"
    echo ""
    kubectl get deployments -n "${NAMESPACE}" -o wide
    echo ""
}

show_rollout_history() {
    local deployment=$1

    log_info "Rollout history for deployment: ${deployment}"
    echo ""
    kubectl rollout history deployment/"${deployment}" -n "${NAMESPACE}"
    echo ""
}

confirm_rollback() {
    local deployment=$1
    local revision=${2:-"previous"}

    echo ""
    log_warn "You are about to rollback deployment: ${deployment}"
    log_warn "Target revision: ${revision}"
    echo ""
    read -p "Are you sure you want to continue? (yes/no): " -r
    echo ""

    if [[ ! $REPLY =~ ^[Yy][Ee][Ss]$ ]]; then
        log_info "Rollback cancelled."
        exit 0
    fi
}

rollback_deployment() {
    local deployment=$1
    local revision=${2:-""}

    log_info "Rolling back deployment: ${deployment}"

    if [[ -n "$revision" ]]; then
        kubectl rollout undo deployment/"${deployment}" -n "${NAMESPACE}" --to-revision="${revision}"
    else
        kubectl rollout undo deployment/"${deployment}" -n "${NAMESPACE}"
    fi

    log_info "Waiting for rollback to complete..."
    kubectl rollout status deployment/"${deployment}" -n "${NAMESPACE}" --timeout=600s

    log_info "Rollback completed successfully!"
}

show_current_status() {
    local deployment=$1

    log_info "Current status of deployment: ${deployment}"
    echo ""
    kubectl get deployment "${deployment}" -n "${NAMESPACE}" -o wide
    echo ""
    kubectl get pods -n "${NAMESPACE}" -l "app=${deployment}" -o wide
    echo ""
}

rollback_all_poc_a() {
    log_info "Rolling back all PoC A (gRPC) components..."

    confirm_rollback "grpc-backend" "all components"

    rollback_deployment "grpc-backend"

    log_info "All PoC A components rolled back successfully!"
    show_current_status "grpc-backend"
}

rollback_all_poc_b() {
    log_info "Rolling back all PoC B (MQTT) components..."

    confirm_rollback "mqtt-backend and emqx" "all components"

    rollback_deployment "mqtt-backend"

    # Note: EMQX is a StatefulSet, handle differently
    if kubectl get statefulset emqx -n "${NAMESPACE}" &> /dev/null; then
        log_info "Rolling back EMQX StatefulSet..."
        kubectl rollout undo statefulset/emqx -n "${NAMESPACE}"
        kubectl rollout status statefulset/emqx -n "${NAMESPACE}" --timeout=600s
    fi

    log_info "All PoC B components rolled back successfully!"
    show_current_status "mqtt-backend"
}

usage() {
    cat <<EOF
Usage: $0 [OPTIONS]

Rollback Kubernetes deployments to previous versions.

OPTIONS:
    -d, --deployment NAME       Rollback specific deployment
    -r, --revision NUMBER       Rollback to specific revision (default: previous)
    -a, --all-poc-a            Rollback all PoC A (gRPC) components
    -b, --all-poc-b            Rollback all PoC B (MQTT) components
    -l, --list                 List all deployments
    -h, --help                 Show this help message

EXAMPLES:
    # List all deployments
    $0 --list

    # Rollback gRPC backend to previous version
    $0 --deployment grpc-backend

    # Rollback MQTT backend to specific revision
    $0 --deployment mqtt-backend --revision 3

    # Rollback all PoC A components
    $0 --all-poc-a

    # Rollback all PoC B components
    $0 --all-poc-b

NOTES:
    - Always check rollout history before rolling back
    - Rollback does not restore ConfigMaps or Secrets
    - StatefulSets (like EMQX) may require manual intervention
    - Use --dry-run with kubectl for testing

EOF
    exit 0
}

#############################################################################
# Main
#############################################################################

main() {
    local deployment=""
    local revision=""
    local action=""

    # Parse arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            -d|--deployment)
                deployment="$2"
                action="single"
                shift 2
                ;;
            -r|--revision)
                revision="$2"
                shift 2
                ;;
            -a|--all-poc-a)
                action="poc-a"
                shift
                ;;
            -b|--all-poc-b)
                action="poc-b"
                shift
                ;;
            -l|--list)
                action="list"
                shift
                ;;
            -h|--help)
                usage
                ;;
            *)
                log_error "Unknown option: $1"
                usage
                ;;
        esac
    done

    check_prerequisites

    case "$action" in
        list)
            list_deployments
            ;;
        single)
            if [[ -z "$deployment" ]]; then
                log_error "Deployment name is required"
                usage
            fi
            show_rollout_history "$deployment"
            confirm_rollback "$deployment" "${revision:-previous}"
            rollback_deployment "$deployment" "$revision"
            show_current_status "$deployment"
            ;;
        poc-a)
            rollback_all_poc_a
            ;;
        poc-b)
            rollback_all_poc_b
            ;;
        *)
            log_error "No action specified"
            usage
            ;;
    esac

    echo ""
    log_info "=========================================="
    log_info "Rollback operation completed!"
    log_info "=========================================="
    echo ""
    echo "Verify the rollback:"
    echo "  kubectl get pods -n ${NAMESPACE}"
    echo "  kubectl logs -n ${NAMESPACE} -l app=<deployment-name>"
    echo ""
}

# Run main function
main "$@"
