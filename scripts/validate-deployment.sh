#!/bin/bash
###
# validate-deployment.sh - Validate deployment is working correctly
# Performs health checks, basic functionality tests, and metrics verification
###

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Configuration
POC_TYPE=${1:-grpc}  # grpc or mqtt
VERBOSE=${VERBOSE:-false}

echo "========================================="
echo "DeltaList Deployment Validation"
echo "PoC Type: $POC_TYPE"
echo "========================================="
echo ""

FAILED_CHECKS=0
TOTAL_CHECKS=0

check() {
    TOTAL_CHECKS=$((TOTAL_CHECKS + 1))
    local name="$1"
    local command="$2"

    echo -n "[$TOTAL_CHECKS] Checking $name... "

    if [ "$VERBOSE" = "true" ]; then
        echo ""
        echo "  Command: $command"
    fi

    if eval "$command" >/dev/null 2>&1; then
        echo -e "${GREEN}OK${NC}"
        return 0
    else
        echo -e "${RED}FAILED${NC}"
        FAILED_CHECKS=$((FAILED_CHECKS + 1))
        return 1
    fi
}

if [ "$POC_TYPE" = "grpc" ]; then
    echo -e "${BLUE}=== Validating gRPC Deployment ===${NC}\n"

    # Check Docker containers
    check "gRPC Backend container" "docker ps | grep -q grpc-backend"
    check "Redis container" "docker ps | grep -q redis"

    # Check health endpoints
    check "Backend health endpoint" "curl -f http://localhost:9090/health"
    check "Backend info endpoint" "curl -f http://localhost:9090/info"

    # Check metrics endpoint
    check "Prometheus metrics endpoint" "curl -f http://localhost:9090/metrics"

    # Check gRPC port is listening
    check "gRPC port 5001 listening" "nc -z localhost 5001"

    # Test simple device connection (if simulator is available)
    if command -v dotnet &> /dev/null; then
        echo ""
        echo -e "${YELLOW}Testing device simulator connection...${NC}"

        timeout 30s dotnet run --project src/GrpcDeviceSimulator/GrpcDeviceSimulator.csproj -- \
            --server localhost:5001 \
            --devices 1 \
            --duration 10 \
            --batch-interval 5 \
            --events-per-batch 5 &>/dev/null && \
        check "Device simulator connection test" "true" || \
        check "Device simulator connection test" "false"
    else
        echo -e "${YELLOW}Dotnet not available, skipping simulator test${NC}"
    fi

    # Check backend metrics
    echo ""
    echo -e "${BLUE}Backend Metrics:${NC}"
    BACKEND_INFO=$(curl -s http://localhost:9090/info)
    echo "$BACKEND_INFO" | jq . 2>/dev/null || echo "$BACKEND_INFO"

elif [ "$POC_TYPE" = "mqtt" ]; then
    echo -e "${BLUE}=== Validating MQTT Deployment ===${NC}\n"

    # Check Docker containers
    check "MQTT Backend container" "docker ps | grep -q mqtt-backend"
    check "EMQX broker container" "docker ps | grep -q emqx"
    check "Redis container" "docker ps | grep -q redis"

    # Check health endpoints
    check "Backend health endpoint" "curl -f http://localhost:8080/health"
    check "Backend info endpoint" "curl -f http://localhost:8080/info"

    # Check EMQX
    check "EMQX Dashboard" "curl -f http://localhost:18083"
    check "EMQX API" "curl -f -u admin:public http://localhost:18083/api/v5/nodes"

    # Check MQTT port
    check "MQTT port 1883 listening" "nc -z localhost 1883"

    # Test MQTT publish/subscribe (if mosquitto clients available)
    if command -v mosquitto_pub &> /dev/null; then
        echo ""
        echo -e "${YELLOW}Testing MQTT pub/sub...${NC}"

        # Subscribe to test topic in background
        timeout 10s mosquitto_sub -h localhost -p 1883 -t "test/validate" -C 1 > /tmp/mqtt_test.txt 2>&1 &
        SUB_PID=$!

        sleep 2

        # Publish test message
        mosquitto_pub -h localhost -p 1883 -t "test/validate" -m "validation_test"

        # Wait for subscriber
        wait $SUB_PID 2>/dev/null && \
        check "MQTT pub/sub test" "grep -q validation_test /tmp/mqtt_test.txt" || \
        check "MQTT pub/sub test" "false"

        rm -f /tmp/mqtt_test.txt
    else
        echo -e "${YELLOW}Mosquitto clients not available, skipping pub/sub test${NC}"
    fi

    # Check backend metrics
    echo ""
    echo -e "${BLUE}Backend Metrics:${NC}"
    BACKEND_INFO=$(curl -s http://localhost:8080/info)
    echo "$BACKEND_INFO" | jq . 2>/dev/null || echo "$BACKEND_INFO"

else
    echo -e "${RED}Unknown PoC type: $POC_TYPE${NC}"
    echo "Usage: $0 [grpc|mqtt]"
    exit 1
fi

# Summary
echo ""
echo "========================================="
echo "Validation Summary"
echo "========================================="
echo "Total checks: $TOTAL_CHECKS"
echo -e "Passed: ${GREEN}$((TOTAL_CHECKS - FAILED_CHECKS))${NC}"
echo -e "Failed: ${RED}$FAILED_CHECKS${NC}"
echo ""

if [ $FAILED_CHECKS -eq 0 ]; then
    echo -e "${GREEN}All checks passed! Deployment is healthy.${NC}"
    exit 0
else
    echo -e "${RED}Some checks failed. Please review the logs.${NC}"
    echo ""
    echo "View logs with:"
    if [ "$POC_TYPE" = "grpc" ]; then
        echo "  docker-compose -f docker-compose.grpc.yml logs"
    else
        echo "  docker-compose -f docker-compose.mqtt.yml logs"
    fi
    exit 1
fi
