#!/bin/bash
#############################################################################
# check-yaml-syntax.sh - Basic YAML syntax validation
#############################################################################
set -euo pipefail

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
K8S_DIR="${PROJECT_ROOT}/deployment/kubernetes"

TOTAL=0
PASSED=0
FAILED=0

log_info() { echo -e "${GREEN}[INFO]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }
log_success() { echo -e "${GREEN}[OK]${NC} $1"; }

# Simple YAML syntax check using Python
validate_yaml() {
    local file=$1
    python3 -c "
import yaml
import sys
try:
    with open('$file', 'r') as f:
        yaml.safe_load_all(f)
    sys.exit(0)
except Exception as e:
    print(f'Error: {e}')
    sys.exit(1)
" 2>&1
}

log_info "Validating YAML syntax for all manifests..."
echo ""

# Find all YAML files
while IFS= read -r file; do
    TOTAL=$((TOTAL + 1))
    filename=$(basename "$file")

    if validate_yaml "$file"; then
        log_success "$filename - Valid YAML"
        PASSED=$((PASSED + 1))
    else
        log_error "$filename - Invalid YAML"
        FAILED=$((FAILED + 1))
    fi
done < <(find "$K8S_DIR" -type f \( -name "*.yaml" -o -name "*.yml" \) | sort)

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "VALIDATION SUMMARY"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Total:  $TOTAL"
echo "Passed: $PASSED"
echo "Failed: $FAILED"
echo ""

if [[ $FAILED -eq 0 ]]; then
    log_success "All YAML files are syntactically valid!"
    exit 0
else
    log_error "$FAILED files have syntax errors!"
    exit 1
fi
