#!/bin/bash

# ============================================
# DeltaList - Docker Validation Script
# ============================================
# This script validates Docker setup and builds

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}============================================${NC}"
echo -e "${BLUE}DeltaList Docker Validation${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""

# Check Docker installation
echo -e "${BLUE}Checking Docker installation...${NC}"
if ! command -v docker &> /dev/null; then
    echo -e "${RED}✗ Docker not found${NC}"
    echo -e "${YELLOW}Install Docker: https://docs.docker.com/get-docker/${NC}"
    exit 1
fi
echo -e "${GREEN}✓ Docker installed: $(docker --version)${NC}"

# Check Docker Compose
echo -e "${BLUE}Checking Docker Compose...${NC}"
if ! command -v docker-compose &> /dev/null && ! docker compose version &> /dev/null; then
    echo -e "${RED}✗ Docker Compose not found${NC}"
    echo -e "${YELLOW}Install Docker Compose: https://docs.docker.com/compose/install/${NC}"
    exit 1
fi
if command -v docker-compose &> /dev/null; then
    echo -e "${GREEN}✓ Docker Compose installed: $(docker-compose --version)${NC}"
else
    echo -e "${GREEN}✓ Docker Compose installed: $(docker compose version)${NC}"
fi

# Check Docker daemon
echo -e "${BLUE}Checking Docker daemon...${NC}"
if ! docker info &> /dev/null; then
    echo -e "${RED}✗ Docker daemon not running${NC}"
    echo -e "${YELLOW}Start Docker daemon${NC}"
    exit 1
fi
echo -e "${GREEN}✓ Docker daemon running${NC}"

# Check disk space
echo -e "${BLUE}Checking disk space...${NC}"
available=$(df -BG . | tail -1 | awk '{print $4}' | sed 's/G//')
if [ "$available" -lt 10 ]; then
    echo -e "${YELLOW}⚠ Low disk space: ${available}GB available (10GB+ recommended)${NC}"
else
    echo -e "${GREEN}✓ Sufficient disk space: ${available}GB available${NC}"
fi

# Check required files
echo -e "${BLUE}Checking required files...${NC}"
files=(
    ".dockerignore"
    "src/GrpcBackend/Dockerfile"
    "src/MqttBackend/Dockerfile"
    "docker-compose.grpc.yml"
    "docker-compose.mqtt.yml"
    "docker-compose.test.yml"
    "scripts/docker-build.sh"
    "scripts/docker-push.sh"
    "scripts/docker-clean.sh"
    "Makefile"
    "docs/DOCKER_GUIDE.md"
)

missing_files=()
for file in "${files[@]}"; do
    if [ -f "$file" ]; then
        echo -e "${GREEN}  ✓ $file${NC}"
    else
        echo -e "${RED}  ✗ $file (missing)${NC}"
        missing_files+=("$file")
    fi
done

if [ ${#missing_files[@]} -gt 0 ]; then
    echo -e "${RED}Missing files found!${NC}"
    exit 1
fi

# Validate Dockerfiles
echo ""
echo -e "${BLUE}Validating Dockerfiles...${NC}"

# Check GrpcBackend Dockerfile
if docker build -f src/GrpcBackend/Dockerfile --target base -t test-grpc-base . &> /dev/null; then
    echo -e "${GREEN}✓ GrpcBackend Dockerfile syntax valid${NC}"
else
    echo -e "${RED}✗ GrpcBackend Dockerfile has errors${NC}"
    exit 1
fi

# Check MqttBackend Dockerfile
if docker build -f src/MqttBackend/Dockerfile --target base -t test-mqtt-base . &> /dev/null; then
    echo -e "${GREEN}✓ MqttBackend Dockerfile syntax valid${NC}"
else
    echo -e "${RED}✗ MqttBackend Dockerfile has errors${NC}"
    exit 1
fi

# Validate docker-compose files
echo ""
echo -e "${BLUE}Validating docker-compose files...${NC}"

if docker-compose -f docker-compose.grpc.yml config &> /dev/null; then
    echo -e "${GREEN}✓ docker-compose.grpc.yml valid${NC}"
else
    echo -e "${RED}✗ docker-compose.grpc.yml has errors${NC}"
    exit 1
fi

if docker-compose -f docker-compose.mqtt.yml config &> /dev/null; then
    echo -e "${GREEN}✓ docker-compose.mqtt.yml valid${NC}"
else
    echo -e "${RED}✗ docker-compose.mqtt.yml has errors${NC}"
    exit 1
fi

if docker-compose -f docker-compose.test.yml config &> /dev/null; then
    echo -e "${GREEN}✓ docker-compose.test.yml valid${NC}"
else
    echo -e "${RED}✗ docker-compose.test.yml has errors${NC}"
    exit 1
fi

# Build images
echo ""
echo -e "${BLUE}Building Docker images...${NC}"
echo -e "${YELLOW}This may take 5-10 minutes...${NC}"
echo ""

# Build GrpcBackend
echo -e "${BLUE}Building GrpcBackend...${NC}"
if docker build -f src/GrpcBackend/Dockerfile -t deltalist/grpc-backend:latest --build-arg BUILD_CONFIGURATION=Release . ; then
    echo -e "${GREEN}✓ GrpcBackend built successfully${NC}"
    grpc_size=$(docker images deltalist/grpc-backend:latest --format "{{.Size}}" | head -1)
    echo -e "${GREEN}  Image size: $grpc_size${NC}"
else
    echo -e "${RED}✗ GrpcBackend build failed${NC}"
    exit 1
fi

echo ""

# Build MqttBackend
echo -e "${BLUE}Building MqttBackend...${NC}"
if docker build -f src/MqttBackend/Dockerfile -t deltalist/mqtt-backend:latest --build-arg BUILD_CONFIGURATION=Release . ; then
    echo -e "${GREEN}✓ MqttBackend built successfully${NC}"
    mqtt_size=$(docker images deltalist/mqtt-backend:latest --format "{{.Size}}" | head -1)
    echo -e "${GREEN}  Image size: $mqtt_size${NC}"
else
    echo -e "${RED}✗ MqttBackend build failed${NC}"
    exit 1
fi

# Summary
echo ""
echo -e "${BLUE}============================================${NC}"
echo -e "${GREEN}Validation Complete!${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""
echo -e "${GREEN}Built Images:${NC}"
echo -e "  - deltalist/grpc-backend:latest ($grpc_size)"
echo -e "  - deltalist/mqtt-backend:latest ($mqtt_size)"
echo ""
echo -e "${YELLOW}Next Steps:${NC}"
echo -e "  1. Start gRPC stack: ${BLUE}make up-grpc${NC}"
echo -e "  2. Start MQTT stack: ${BLUE}make up-mqtt${NC}"
echo -e "  3. Run tests: ${BLUE}make test${NC}"
echo -e "  4. View documentation: ${BLUE}docs/DOCKER_GUIDE.md${NC}"
echo ""

# Cleanup test images
docker rmi test-grpc-base test-mqtt-base 2>/dev/null || true
