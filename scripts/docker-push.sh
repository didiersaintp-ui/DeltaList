#!/bin/bash

# ============================================
# DeltaList - Docker Push Script
# ============================================
# This script pushes Docker images to Azure Container Registry (ACR)

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
TAG="${TAG:-latest}"

# Azure Container Registry configuration
ACR_NAME="${ACR_NAME:-}"
ACR_LOGIN_SERVER="${ACR_LOGIN_SERVER:-}"

# Default registry
REGISTRY="${REGISTRY:-deltalist}"

# Image names
GRPC_IMAGE="${REGISTRY}/grpc-backend:${TAG}"
MQTT_IMAGE="${REGISTRY}/mqtt-backend:${TAG}"

echo -e "${BLUE}============================================${NC}"
echo -e "${BLUE}DeltaList Docker Push${NC}"
echo -e "${BLUE}============================================${NC}"

# Check if pushing to ACR
if [[ -n "${ACR_NAME}" && -n "${ACR_LOGIN_SERVER}" ]]; then
    echo -e "${YELLOW}Target: Azure Container Registry${NC}"
    echo -e "${YELLOW}ACR Name: ${ACR_NAME}${NC}"
    echo -e "${YELLOW}ACR Server: ${ACR_LOGIN_SERVER}${NC}"
    echo ""

    # Login to ACR
    echo -e "${BLUE}Logging in to Azure Container Registry...${NC}"
    if command -v az &> /dev/null; then
        if az acr login --name "${ACR_NAME}"; then
            echo -e "${GREEN}✓ Successfully logged in to ACR${NC}"
        else
            echo -e "${RED}✗ Failed to login to ACR${NC}"
            echo -e "${YELLOW}Make sure you are logged in to Azure CLI: az login${NC}"
            exit 1
        fi
    else
        echo -e "${RED}✗ Azure CLI not found${NC}"
        echo -e "${YELLOW}Please install Azure CLI: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli${NC}"
        exit 1
    fi

    # Tag images for ACR
    ACR_GRPC_IMAGE="${ACR_LOGIN_SERVER}/grpc-backend:${TAG}"
    ACR_MQTT_IMAGE="${ACR_LOGIN_SERVER}/mqtt-backend:${TAG}"

    echo ""
    echo -e "${BLUE}Tagging images for ACR...${NC}"
    docker tag "${GRPC_IMAGE}" "${ACR_GRPC_IMAGE}"
    docker tag "${MQTT_IMAGE}" "${ACR_MQTT_IMAGE}"
    echo -e "${GREEN}✓ Images tagged${NC}"

    # Update image variables for push
    PUSH_GRPC_IMAGE="${ACR_GRPC_IMAGE}"
    PUSH_MQTT_IMAGE="${ACR_MQTT_IMAGE}"
else
    echo -e "${YELLOW}Target: Docker Hub / Local Registry${NC}"
    echo -e "${YELLOW}Registry: ${REGISTRY}${NC}"
    echo ""
    echo -e "${YELLOW}Note: To push to Azure Container Registry, set:${NC}"
    echo -e "${YELLOW}  export ACR_NAME=your-acr-name${NC}"
    echo -e "${YELLOW}  export ACR_LOGIN_SERVER=your-acr-name.azurecr.io${NC}"
    echo ""

    # Check if logged in to Docker Hub
    if ! docker info | grep -q "Username"; then
        echo -e "${YELLOW}Warning: You may not be logged in to Docker Hub${NC}"
        echo -e "${YELLOW}Run: docker login${NC}"
        read -p "Continue anyway? (y/N) " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            exit 1
        fi
    fi

    PUSH_GRPC_IMAGE="${GRPC_IMAGE}"
    PUSH_MQTT_IMAGE="${MQTT_IMAGE}"
fi

# Function to push an image
push_image() {
    local name=$1
    local image=$2

    echo ""
    echo -e "${BLUE}Pushing ${name}...${NC}"

    if docker push "${image}"; then
        echo -e "${GREEN}✓ ${name} pushed successfully${NC}"
        return 0
    else
        echo -e "${RED}✗ Failed to push ${name}${NC}"
        return 1
    fi
}

# Push images
if ! push_image "gRPC Backend" "${PUSH_GRPC_IMAGE}"; then
    exit 1
fi

if ! push_image "MQTT Backend" "${PUSH_MQTT_IMAGE}"; then
    exit 1
fi

# Summary
echo ""
echo -e "${BLUE}============================================${NC}"
echo -e "${GREEN}Push Complete!${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""
echo -e "${GREEN}Images pushed:${NC}"
echo -e "  - ${PUSH_GRPC_IMAGE}"
echo -e "  - ${PUSH_MQTT_IMAGE}"
echo ""

if [[ -n "${ACR_NAME}" && -n "${ACR_LOGIN_SERVER}" ]]; then
    echo -e "${YELLOW}To pull from ACR:${NC}"
    echo -e "  docker pull ${ACR_GRPC_IMAGE}"
    echo -e "  docker pull ${ACR_MQTT_IMAGE}"
else
    echo -e "${YELLOW}To pull from registry:${NC}"
    echo -e "  docker pull ${PUSH_GRPC_IMAGE}"
    echo -e "  docker pull ${PUSH_MQTT_IMAGE}"
fi
echo ""
