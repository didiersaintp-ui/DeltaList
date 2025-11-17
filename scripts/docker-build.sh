#!/bin/bash

# ============================================
# DeltaList - Docker Build Script
# ============================================
# This script builds all Docker images for the DeltaList project

set -e  # Exit on error
set -u  # Exit on undefined variable

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
BUILD_CONFIG="${BUILD_CONFIG:-Release}"
TAG="${TAG:-latest}"
REGISTRY="${REGISTRY:-deltalist}"

# Image names
GRPC_IMAGE="${REGISTRY}/grpc-backend:${TAG}"
MQTT_IMAGE="${REGISTRY}/mqtt-backend:${TAG}"

echo -e "${BLUE}============================================${NC}"
echo -e "${BLUE}DeltaList Docker Build${NC}"
echo -e "${BLUE}============================================${NC}"
echo -e "${YELLOW}Build Configuration: ${BUILD_CONFIG}${NC}"
echo -e "${YELLOW}Tag: ${TAG}${NC}"
echo -e "${YELLOW}Registry: ${REGISTRY}${NC}"
echo ""

# Function to build an image
build_image() {
    local name=$1
    local dockerfile=$2
    local image=$3

    echo -e "${BLUE}Building ${name}...${NC}"

    if docker build \
        --file "${PROJECT_ROOT}/${dockerfile}" \
        --tag "${image}" \
        --build-arg BUILD_CONFIGURATION="${BUILD_CONFIG}" \
        --progress=plain \
        "${PROJECT_ROOT}"; then
        echo -e "${GREEN}✓ ${name} built successfully${NC}"

        # Show image size
        size=$(docker images "${image}" --format "{{.Size}}" | head -1)
        echo -e "${GREEN}  Image size: ${size}${NC}"
        echo ""
        return 0
    else
        echo -e "${RED}✗ Failed to build ${name}${NC}"
        return 1
    fi
}

# Build gRPC Backend
if ! build_image "gRPC Backend" "src/GrpcBackend/Dockerfile" "${GRPC_IMAGE}"; then
    exit 1
fi

# Build MQTT Backend
if ! build_image "MQTT Backend" "src/MqttBackend/Dockerfile" "${MQTT_IMAGE}"; then
    exit 1
fi

# Summary
echo -e "${BLUE}============================================${NC}"
echo -e "${GREEN}Build Complete!${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""
echo -e "${GREEN}Images built:${NC}"
echo -e "  - ${GRPC_IMAGE}"
echo -e "  - ${MQTT_IMAGE}"
echo ""
echo -e "${YELLOW}To run locally:${NC}"
echo -e "  docker-compose -f docker-compose.grpc.yml up"
echo -e "  docker-compose -f docker-compose.mqtt.yml up"
echo ""
echo -e "${YELLOW}To push to registry:${NC}"
echo -e "  ./scripts/docker-push.sh"
echo ""
