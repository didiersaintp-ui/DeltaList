#!/bin/bash
###
# test_comparison.sh - Compare gRPC vs MQTT performance
# Lance les 2 PoC en parallèle avec les mêmes paramètres et compare les métriques
###

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo "========================================="
echo "PoC Comparison Test (gRPC vs MQTT)"
echo "========================================="

# Configuration
TEST_DURATION=${TEST_DURATION:-120}
NUM_DEVICES=${NUM_DEVICES:-10}
BATCH_INTERVAL=${BATCH_INTERVAL:-20}
EVENTS_PER_BATCH=${EVENTS_PER_BATCH:-50}

echo "Test Parameters:"
echo "  Duration: ${TEST_DURATION}s"
echo "  Devices: ${NUM_DEVICES}"
echo "  Batch Interval: ${BATCH_INTERVAL}s"
echo "  Events per Batch: ${EVENTS_PER_BATCH}"
echo ""

# Create test directory
TEST_DIR="test_results_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$TEST_DIR"

echo -e "${BLUE}[1/5] Starting gRPC infrastructure...${NC}"
docker-compose -f docker-compose.grpc.yml up -d
sleep 10

echo -e "${BLUE}[2/5] Running gRPC simulator...${NC}"
dotnet run --project src/GrpcDeviceSimulator/GrpcDeviceSimulator.csproj -- \
    --server localhost:5001 \
    --devices $NUM_DEVICES \
    --duration $TEST_DURATION \
    --batch-interval $BATCH_INTERVAL \
    --events-per-batch $EVENTS_PER_BATCH \
    --export-metrics \
    --metrics-dir "$TEST_DIR/grpc" &

GRPC_PID=$!

# Wait for gRPC test to complete
wait $GRPC_PID
echo -e "${GREEN}gRPC test completed${NC}"

# Stop gRPC infrastructure
docker-compose -f docker-compose.grpc.yml down
sleep 5

echo -e "${BLUE}[3/5] Starting MQTT infrastructure...${NC}"
docker-compose -f docker-compose.mqtt.yml up -d
sleep 15

echo -e "${BLUE}[4/5] Running MQTT simulator...${NC}"
dotnet run --project src/MqttDeviceSimulator/MqttDeviceSimulator.csproj -- \
    --server localhost \
    --port 1883 \
    --devices $NUM_DEVICES \
    --duration $TEST_DURATION \
    --batch-interval $BATCH_INTERVAL \
    --events-per-batch $EVENTS_PER_BATCH \
    --qos 1 \
    --export-metrics \
    --metrics-dir "$TEST_DIR/mqtt" &

MQTT_PID=$!

# Wait for MQTT test to complete
wait $MQTT_PID
echo -e "${GREEN}MQTT test completed${NC}"

# Stop MQTT infrastructure
docker-compose -f docker-compose.mqtt.yml down

echo -e "${BLUE}[5/5] Analyzing results...${NC}"

# Run Python analysis script if available
if [ -f "tests/load-tests/analyze_results.py" ]; then
    python3 tests/load-tests/analyze_results.py \
        --grpc-dir "$TEST_DIR/grpc" \
        --mqtt-dir "$TEST_DIR/mqtt" \
        --output "$TEST_DIR/comparison_report.html"

    echo -e "${GREEN}Analysis complete! Report: $TEST_DIR/comparison_report.html${NC}"
else
    echo -e "${YELLOW}Analysis script not found, showing basic comparison:${NC}"

    echo ""
    echo "========== gRPC Metrics =========="
    cat "$TEST_DIR/grpc/grpc_metrics_"*.csv | head -30

    echo ""
    echo "========== MQTT Metrics =========="
    cat "$TEST_DIR/mqtt/mqtt_metrics_"*.csv | head -30
fi

echo ""
echo "========================================="
echo -e "${GREEN}Comparison test completed!${NC}"
echo "Results saved in: $TEST_DIR"
echo "========================================="
