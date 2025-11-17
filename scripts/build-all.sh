#!/bin/bash
# ============================================
# DeltaList Complete Build Script
# ============================================
# This script builds all projects in the correct dependency order
# with proper error handling and validation
# ============================================

set -e  # Exit on error
set -u  # Exit on undefined variable
set -o pipefail  # Exit on pipe failure

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
BUILD_CONFIG="${BUILD_CONFIG:-Release}"
PROJECT_ROOT="/home/user/DeltaList"
VERBOSE="${VERBOSE:-false}"

# Counters
SUCCESS_COUNT=0
TOTAL_COUNT=0

# ============================================
# Helper Functions
# ============================================

print_header() {
    echo ""
    echo -e "${BLUE}============================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}============================================${NC}"
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠ $1${NC}"
}

print_info() {
    echo -e "${BLUE}ℹ $1${NC}"
}

build_project() {
    local project_name=$1
    local project_path=$2

    TOTAL_COUNT=$((TOTAL_COUNT + 1))

    print_header "Building: $project_name"

    if [ ! -f "$project_path" ]; then
        print_error "Project file not found: $project_path"
        return 1
    fi

    print_info "Restoring dependencies..."
    if [ "$VERBOSE" = "true" ]; then
        dotnet restore "$project_path"
    else
        dotnet restore "$project_path" > /dev/null 2>&1
    fi

    if [ $? -eq 0 ]; then
        print_success "Dependencies restored"
    else
        print_error "Failed to restore dependencies"
        return 1
    fi

    print_info "Building project (Configuration: $BUILD_CONFIG)..."
    if [ "$VERBOSE" = "true" ]; then
        dotnet build "$project_path" \
            --configuration "$BUILD_CONFIG" \
            --no-restore
    else
        dotnet build "$project_path" \
            --configuration "$BUILD_CONFIG" \
            --no-restore \
            > /dev/null 2>&1
    fi

    if [ $? -eq 0 ]; then
        print_success "$project_name built successfully"
        SUCCESS_COUNT=$((SUCCESS_COUNT + 1))
        return 0
    else
        print_error "$project_name build failed"

        # Show detailed error on failure
        print_warning "Re-running with detailed output..."
        dotnet build "$project_path" \
            --configuration "$BUILD_CONFIG" \
            --no-restore
        return 1
    fi
}

# ============================================
# Pre-build Checks
# ============================================

print_header "Pre-Build Validation"

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    print_error "dotnet CLI not found. Please install .NET 8 SDK"
    echo ""
    print_info "To install .NET 8 SDK:"
    echo "  Ubuntu/Debian: wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh && bash dotnet-install.sh --channel 8.0"
    echo "  Or visit: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
fi

# Check dotnet version
DOTNET_VERSION=$(dotnet --version)
print_success "dotnet CLI found (version: $DOTNET_VERSION)"

# Check if .NET 8 SDK is installed
if ! dotnet --list-sdks | grep -q "^8\."; then
    print_error ".NET 8 SDK not found"
    echo ""
    print_info "Installed SDKs:"
    dotnet --list-sdks
    echo ""
    print_info "Please install .NET 8 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
fi

print_success ".NET 8 SDK is installed"

# Change to project root
cd "$PROJECT_ROOT" || exit 1
print_success "Working directory: $PROJECT_ROOT"

# Check if solution file exists
if [ ! -f "DeltaList.sln" ]; then
    print_error "Solution file not found: DeltaList.sln"
    exit 1
fi

print_success "Solution file found: DeltaList.sln"

echo ""
print_info "Build Configuration: $BUILD_CONFIG"
print_info "Verbose Mode: $VERBOSE"

# ============================================
# Build Projects in Dependency Order
# ============================================

print_header "Starting Build Process"

# Step 1: Build Shared.Models (no dependencies)
build_project "Shared.Models" "$PROJECT_ROOT/src/Shared.Models/Shared.Models.csproj" || exit 1

# Step 2: Build GrpcBackend (depends on Shared.Models)
build_project "GrpcBackend" "$PROJECT_ROOT/src/GrpcBackend/GrpcBackend.csproj" || exit 1

# Step 3: Build MqttBackend (depends on Shared.Models)
build_project "MqttBackend" "$PROJECT_ROOT/src/MqttBackend/MqttBackend.csproj" || exit 1

# Step 4: Build GrpcDeviceSimulator (depends on Shared.Models)
build_project "GrpcDeviceSimulator" "$PROJECT_ROOT/src/GrpcDeviceSimulator/GrpcDeviceSimulator.csproj" || exit 1

# Step 5: Build MqttDeviceSimulator (depends on Shared.Models)
build_project "MqttDeviceSimulator" "$PROJECT_ROOT/src/MqttDeviceSimulator/MqttDeviceSimulator.csproj" || exit 1

# ============================================
# Build Solution (Verification)
# ============================================

print_header "Verifying Complete Solution Build"

print_info "Building entire solution..."
if [ "$VERBOSE" = "true" ]; then
    dotnet build "$PROJECT_ROOT/DeltaList.sln" \
        --configuration "$BUILD_CONFIG" \
        --no-restore
else
    dotnet build "$PROJECT_ROOT/DeltaList.sln" \
        --configuration "$BUILD_CONFIG" \
        --no-restore \
        > /dev/null 2>&1
fi

if [ $? -eq 0 ]; then
    print_success "Solution build verification successful"
else
    print_error "Solution build verification failed"

    # Show detailed error
    print_warning "Re-running with detailed output..."
    dotnet build "$PROJECT_ROOT/DeltaList.sln" \
        --configuration "$BUILD_CONFIG" \
        --no-restore
    exit 1
fi

# ============================================
# Build Summary
# ============================================

print_header "Build Summary"

echo ""
print_info "Configuration: $BUILD_CONFIG"
print_info "Total Projects: $TOTAL_COUNT"
print_success "Successful Builds: $SUCCESS_COUNT"

if [ $SUCCESS_COUNT -eq $TOTAL_COUNT ]; then
    echo ""
    print_success "============================================"
    print_success "    ALL PROJECTS BUILT SUCCESSFULLY"
    print_success "============================================"
    echo ""
    print_info "Build artifacts location:"
    echo "  - Shared.Models: $PROJECT_ROOT/src/Shared.Models/bin/$BUILD_CONFIG/net8.0/"
    echo "  - GrpcBackend: $PROJECT_ROOT/src/GrpcBackend/bin/$BUILD_CONFIG/net8.0/"
    echo "  - MqttBackend: $PROJECT_ROOT/src/MqttBackend/bin/$BUILD_CONFIG/net8.0/"
    echo "  - GrpcDeviceSimulator: $PROJECT_ROOT/src/GrpcDeviceSimulator/bin/$BUILD_CONFIG/net8.0/"
    echo "  - MqttDeviceSimulator: $PROJECT_ROOT/src/MqttDeviceSimulator/bin/$BUILD_CONFIG/net8.0/"
    echo ""
    print_info "Next steps:"
    echo "  1. Run tests: ./scripts/test-all.sh"
    echo "  2. Build Docker images: docker-compose build"
    echo "  3. Start services: docker-compose -f docker-compose.grpc.yml up -d"
    echo ""
    exit 0
else
    FAILED_COUNT=$((TOTAL_COUNT - SUCCESS_COUNT))
    echo ""
    print_error "============================================"
    print_error "    BUILD FAILED"
    print_error "============================================"
    echo ""
    print_error "Failed: $FAILED_COUNT project(s)"
    echo ""
    print_info "To see detailed build output, run:"
    echo "  VERBOSE=true ./scripts/build-all.sh"
    echo ""
    exit 1
fi
