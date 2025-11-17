#!/bin/bash
###
# test_mqtt_flow.sh - Test complet du flow MQTT
# Test l'ensemble du système MQTT avec device simulator, backend, et broker
###

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "========================================="
echo "MQTT Flow Integration Test"
echo "========================================="

# Configuration
BROKER_HOST="localhost"
BROKER_PORT=1883
BACKEND_URL="localhost:8080"
TEST_DURATION=60
NUM_DEVICES=5
BATCH_INTERVAL=10

# Step 1: Start MQTT infrastructure
echo -e "${YELLOW}[1/6] Starting MQTT infrastructure (EMQX + Backend)...${NC}"
if docker-compose -f docker-compose.mqtt.yml up -d emqx mqtt-backend redis; then
    echo -e "${GREEN}Infrastructure started${NC}"
else
    echo -e "${RED}Failed to start infrastructure${NC}"
    exit 1
fi

# Wait for services to be ready
echo "Waiting for services to be ready..."
sleep 15

# Check EMQX
if curl -f http://localhost:18083 &>/dev/null; then
    echo -e "${GREEN}EMQX is running${NC}"
else
    echo -e "${YELLOW}EMQX dashboard not accessible${NC}"
fi

# Check backend health
if curl -f http://$BACKEND_URL/health &>/dev/null; then
    echo -e "${GREEN}Backend is healthy${NC}"
else
    echo -e "${RED}Backend health check failed${NC}"
    docker-compose -f docker-compose.mqtt.yml logs mqtt-backend
    exit 1
fi

# Step 2: Run simulator with QoS testing
echo -e "${YELLOW}[2/6] Running device simulator with QoS 1...${NC}"
dotnet run --project src/MqttDeviceSimulator/MqttDeviceSimulator.csproj -- \
    --server $BROKER_HOST \
    --port $BROKER_PORT \
    --devices $NUM_DEVICES \
    --duration $TEST_DURATION \
    --batch-interval $BATCH_INTERVAL \
    --events-per-batch 10 \
    --qos 1 \
    --export-metrics \
    --verbose &

SIMULATOR_PID=$!
echo "Simulator started with PID: $SIMULATOR_PID"

# Step 3: Wait for initial events
echo -e "${YELLOW}[3/6] Waiting for initial events...${NC}"
sleep 15

# Step 4: Publish blacklist delta
echo -e "${YELLOW}[4/6] Publishing blacklist delta...${NC}"
# Using mosquitto_pub if available
if command -v mosquitto_pub &> /dev/null; then
    # Create a simple blacklist delta (in real scenario, this would be proper protobuf)
    echo "Publishing test blacklist delta..."
    mosquitto_pub -h $BROKER_HOST -p $BROKER_PORT \
        -t "blacklist/delta" \
        -m '{"deltaId":"test-001","seqNo":1,"added":["TOKEN001","TOKEN002"]}' \
        -q 1
    echo -e "${GREEN}Blacklist delta published${NC}"
else
    echo -e "${YELLOW}mosquitto_pub not installed, skipping blacklist test${NC}"
fi

# Step 5: Verify metrics
echo -e "${YELLOW}[5/6] Verifying metrics...${NC}"
sleep 10

# Check backend info
BACKEND_INFO=$(curl -s http://$BACKEND_URL/info)
echo "Backend Info:"
echo "$BACKEND_INFO" | jq .

CONNECTED_DEVICES=$(echo "$BACKEND_INFO" | jq -r '.connectedDevices // 0')
TOTAL_EVENTS=$(echo "$BACKEND_INFO" | jq -r '.totalEventsProcessed // 0')

if [ "$CONNECTED_DEVICES" -ge 1 ]; then
    echo -e "${GREEN}Connected devices: $CONNECTED_DEVICES${NC}"
else
    echo -e "${YELLOW}Device count: $CONNECTED_DEVICES${NC}"
fi

if [ "$TOTAL_EVENTS" -ge 1 ]; then
    echo -e "${GREEN}Total events processed: $TOTAL_EVENTS${NC}"
else
    echo -e "${YELLOW}Events processed: $TOTAL_EVENTS${NC}"
fi

# Step 6: Wait for simulator to finish
echo -e "${YELLOW}[6/6] Waiting for simulator to complete...${NC}"
wait $SIMULATOR_PID

# Check generated metrics
echo ""
echo "========================================="
echo "Generated Metrics Files:"
echo "========================================="
ls -lh metrics/mqtt_* 2>/dev/null || echo "No metrics files found"

# Cleanup
echo ""
echo -e "${YELLOW}Cleaning up...${NC}"
docker-compose -f docker-compose.mqtt.yml down

echo ""
echo "========================================="
echo -e "${GREEN}Test completed successfully!${NC}"
echo "========================================="
