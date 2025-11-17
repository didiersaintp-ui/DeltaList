#!/bin/bash
#############################################################################
# validate-manifests.sh - Validate Kubernetes manifests
#############################################################################
set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
K8S_DIR="${PROJECT_ROOT}/deployment/kubernetes"

# Counters
TOTAL_FILES=0
PASSED_FILES=0
FAILED_FILES=0
WARNINGS=0

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

log_success() {
    echo -e "${GREEN}[OK]${NC} $1"
}

check_prerequisites() {
    log_info "Checking prerequisites..."

    if ! command -v kubectl &> /dev/null; then
        log_error "kubectl not found. Please install kubectl."
        exit 1
    fi

    log_info "kubectl version: $(kubectl version --client --short 2>/dev/null || kubectl version --client)"
}

validate_yaml_syntax() {
    local file=$1
    log_info "Validating YAML syntax: $(basename "$file")"

    # Basic YAML parsing with kubectl
    if kubectl apply --dry-run=client -f "$file" &> /dev/null; then
        log_success "$(basename "$file") - Valid YAML syntax"
        return 0
    else
        log_error "$(basename "$file") - Invalid YAML syntax"
        kubectl apply --dry-run=client -f "$file" 2>&1 | head -20
        return 1
    fi
}

validate_with_kubeval() {
    local file=$1

    if ! command -v kubeval &> /dev/null; then
        log_warn "kubeval not found. Skipping schema validation."
        log_warn "Install kubeval: https://kubeval.instrumenta.dev/"
        return 0
    fi

    log_info "Validating with kubeval: $(basename "$file")"

    if kubeval --strict --ignore-missing-schemas "$file"; then
        log_success "$(basename "$file") - Passed kubeval validation"
        return 0
    else
        log_error "$(basename "$file") - Failed kubeval validation"
        return 1
    fi
}

validate_with_kube_score() {
    local file=$1

    if ! command -v kube-score &> /dev/null; then
        log_warn "kube-score not found. Skipping quality check."
        log_warn "Install kube-score: https://kube-score.com/"
        return 0
    fi

    log_info "Checking quality with kube-score: $(basename "$file")"

    local output
    output=$(kube-score score "$file" 2>&1 || true)

    # Count warnings and errors
    local warnings_count
    warnings_count=$(echo "$output" | grep -c "^\[WARNING\]" || true)

    local critical_count
    critical_count=$(echo "$output" | grep -c "^\[CRITICAL\]" || true)

    if [[ $critical_count -gt 0 ]]; then
        log_error "$(basename "$file") - $critical_count critical issues found"
        echo "$output" | grep "^\[CRITICAL\]"
        return 1
    elif [[ $warnings_count -gt 0 ]]; then
        log_warn "$(basename "$file") - $warnings_count warnings found"
        echo "$output" | grep "^\[WARNING\]" | head -10
        WARNINGS=$((WARNINGS + warnings_count))
        return 0
    else
        log_success "$(basename "$file") - Passed quality check"
        return 0
    fi
}

validate_templates() {
    log_info "Validating secret templates..."

    # Check for placeholder variables
    local templates=(
        "${K8S_DIR}/grpc/secret-template.yaml"
        "${K8S_DIR}/mqtt/secret-template.yaml"
    )

    for template in "${templates[@]}"; do
        if [[ -f "$template" ]]; then
            log_info "Checking template: $(basename "$template")"

            # Look for placeholders
            if grep -q '\${' "$template"; then
                log_success "$(basename "$template") - Contains placeholders (OK)"
            else
                log_warn "$(basename "$template") - No placeholders found"
            fi

            # Ensure no hardcoded secrets
            if grep -qi "password.*:.*[a-zA-Z0-9]\{10,\}" "$template"; then
                log_error "$(basename "$template") - Potential hardcoded secret found!"
                FAILED_FILES=$((FAILED_FILES + 1))
            fi
        fi
    done
}

validate_configmaps() {
    log_info "Validating ConfigMaps..."

    local configmaps=(
        "${K8S_DIR}/grpc/configmap.yaml"
        "${K8S_DIR}/mqtt/configmap.yaml"
    )

    for cm in "${configmaps[@]}"; do
        if [[ -f "$cm" ]]; then
            # Check if JSON inside ConfigMap is valid
            if grep -q "appsettings.Production.json:" "$cm"; then
                log_info "Validating JSON in $(basename "$cm")"

                # Extract JSON (simple approach, may need refinement)
                # For now, just validate YAML structure
                if kubectl apply --dry-run=client -f "$cm" &> /dev/null; then
                    log_success "$(basename "$cm") - Valid"
                else
                    log_error "$(basename "$cm") - Invalid"
                    FAILED_FILES=$((FAILED_FILES + 1))
                fi
            fi
        fi
    done
}

validate_resource_limits() {
    log_info "Checking resource limits..."

    local files=(
        "${K8S_DIR}/grpc/deployment.yaml"
        "${K8S_DIR}/mqtt/mqtt-backend-deployment.yaml"
        "${K8S_DIR}/mqtt/emqx-deployment.yaml"
        "${K8S_DIR}/grpc/redis.yaml"
    )

    for file in "${files[@]}"; do
        if [[ -f "$file" ]]; then
            if grep -q "resources:" "$file" && \
               grep -q "limits:" "$file" && \
               grep -q "requests:" "$file"; then
                log_success "$(basename "$file") - Has resource limits and requests"
            else
                log_warn "$(basename "$file") - Missing resource limits or requests"
                WARNINGS=$((WARNINGS + 1))
            fi
        fi
    done
}

validate_health_probes() {
    log_info "Checking health probes..."

    local files=(
        "${K8S_DIR}/grpc/deployment.yaml"
        "${K8S_DIR}/mqtt/mqtt-backend-deployment.yaml"
        "${K8S_DIR}/mqtt/emqx-deployment.yaml"
    )

    for file in "${files[@]}"; do
        if [[ -f "$file" ]]; then
            if grep -q "livenessProbe:" "$file" && \
               grep -q "readinessProbe:" "$file"; then
                log_success "$(basename "$file") - Has liveness and readiness probes"
            else
                log_warn "$(basename "$file") - Missing health probes"
                WARNINGS=$((WARNINGS + 1))
            fi
        fi
    done
}

validate_all_manifests() {
    log_info "Finding all Kubernetes manifests..."

    # Find all YAML files (exclude templates and kustomization files)
    local manifests
    manifests=$(find "$K8S_DIR" -type f \
        \( -name "*.yaml" -o -name "*.yml" \) \
        ! -name "*template*" \
        ! -name "kustomization.yaml" \
        ! -name "values.yaml" | sort)

    echo ""
    log_info "Found manifests:"
    echo "$manifests" | while read -r f; do
        echo "  - ${f#$PROJECT_ROOT/}"
    done
    echo ""

    # Validate each manifest
    while IFS= read -r file; do
        TOTAL_FILES=$((TOTAL_FILES + 1))
        echo ""
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

        if validate_yaml_syntax "$file"; then
            validate_with_kubeval "$file" || FAILED_FILES=$((FAILED_FILES + 1))
            validate_with_kube_score "$file" || FAILED_FILES=$((FAILED_FILES + 1))
            PASSED_FILES=$((PASSED_FILES + 1))
        else
            FAILED_FILES=$((FAILED_FILES + 1))
        fi

        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    done <<< "$manifests"
}

generate_report() {
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "                  VALIDATION REPORT"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "Total files:    $TOTAL_FILES"
    echo "Passed:         $PASSED_FILES"
    echo "Failed:         $FAILED_FILES"
    echo "Warnings:       $WARNINGS"
    echo ""

    if [[ $FAILED_FILES -eq 0 ]]; then
        log_success "All manifests are valid!"
        if [[ $WARNINGS -gt 0 ]]; then
            log_warn "However, there are $WARNINGS warnings to review."
        fi
        echo ""
        echo "Next steps:"
        echo "  1. Review warnings and optimize manifests"
        echo "  2. Test deployment with: kubectl apply --dry-run=server"
        echo "  3. Deploy to development environment first"
        echo ""
        return 0
    else
        log_error "$FAILED_FILES manifests failed validation!"
        echo ""
        echo "Please fix the errors above before deploying."
        echo ""
        return 1
    fi
}

#############################################################################
# Main
#############################################################################

main() {
    log_info "=========================================="
    log_info "Kubernetes Manifest Validation"
    log_info "=========================================="
    echo ""

    check_prerequisites

    # Run all validations
    validate_all_manifests
    validate_templates
    validate_configmaps
    validate_resource_limits
    validate_health_probes

    # Generate final report
    generate_report
}

# Run main function
main "$@"
