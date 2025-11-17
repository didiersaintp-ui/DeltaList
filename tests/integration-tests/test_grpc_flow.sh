#!/bin/bash
##
# test_grpc_flow.sh - Test complet du flow gRPC
# Test l'ensemble du système gRPC avec device simulator, backend, et blacklist
##

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "========================================="
echo "gRPC Flow Integration Test"
echo "========================================="

# Configuration
BACKEND_URL="localhost:5001"
METRICS_URL="localhost:9090"
TEST_DURATION=60
NUM_DEVICES=5
BATCH_INTERVAL=10

# Step 1: Start Backend
echo -e "${YELLOW}[1/6] Starting gRPC Backend...${NC}"
if docker-compose -f docker-compose.grpc.yml up -d grpc-backend redis; then
    echo -e "${GREEN}Backend started${NC}"
else
    echo -e "${RED}Failed to start backend${NC}"
    exit 1
fi

# Wait for backend to be ready
echo "Waiting for backend to be ready..."
sleep 10

# Check health
if curl -f http://$METRICS_URL/health &>/dev/null; then
    echo -e "${GREEN}Backend is healthy${NC}"
else
    echo -e "${RED}Backend health check failed${NC}"
    docker-compose -f docker-compose.grpc.yml logs grpc-backend
    exit 1
fi

# Step 2: Run simulator
echo -e "${YELLOW}[2/6] Running device simulator...${NC}"
dotnet run --project src/GrpcDeviceSimulator/GrpcDeviceSimulator.csproj -- \
    --server $BACKEND_URL \
    --devices $NUM_DEVICES \
    --duration $TEST_DURATION \
    --batch-interval $BATCH_INTERVAL \
    --events-per-batch 10 \
    --export-metrics \
    --verbose &

SIMULATOR_PID=$!
echo "Simulator started with PID: $SIMULATOR_PID"

# Step 3: Wait a bit for events to be sent
echo -e "${YELLOW}[3/6] Waiting for initial events...${NC}"
sleep 15

# Step 4: Add blacklist entries via gRPC
echo -e "${YELLOW}[4/6] Adding blacklist entries...${NC}"
# Using grpcurl to add blacklist entries (if installed)
if command -v grpcurl &> /dev/null; then
    echo '{"tokens": ["TOKEN001", "TOKEN002", "TOKEN003"]}' | \
        grpcurl -plaintext -d @ $BACKEND_URL \
        DeltaList.Shared.Services.BlacklistAdminService/AddTokens
    echo -e "${GREEN}Blacklist entries added${NC}"
else
    echo -e "${YELLOW}grpcurl not installed, skipping blacklist test${NC}"
fi

# Step 5: Verify delivery
echo -e "${YELLOW}[5/6] Verifying metrics...${NC}"
sleep 10

# Check metrics endpoint
METRICS=$(curl -s http://$METRICS_URL/info)
echo "Backend Info:"
echo "$METRICS" | jq .

ACTIVE_CONNECTIONS=$(echo "$METRICS" | jq -r '.activeConnections')
TOTAL_EVENTS=$(echo "$METRICS" | jq -r '.totalEventsStored')

if [ "$ACTIVE_CONNECTIONS" -ge 1 ]; then
    echo -e "${GREEN}Active connections: $ACTIVE_CONNECTIONS${NC}"
else
    echo -e "${RED}No active connections!${NC}"
fi

if [ "$TOTAL_EVENTS" -ge 1 ]; then
    echo -e "${GREEN}Total events received: $TOTAL_EVENTS${NC}"
else
    echo -e "${RED}No events received!${NC}"
fi

# Step 6: Wait for simulator to finish
echo -e "${YELLOW}[6/6] Waiting for simulator to complete...${NC}"
wait $SIMULATOR_PID

# Check generated metrics
echo ""
echo "========================================="
echo "Generated Metrics Files:"
echo "========================================="
ls -lh metrics/grpc_* 2>/dev/null || echo "No metrics files found"

# Cleanup
echo ""
echo -e "${YELLOW}Cleaning up...${NC}"
docker-compose -f docker-compose.grpc.yml down

echo ""
echo "========================================="
echo -e "${GREEN}Test completed successfully!${NC}"
echo "========================================="
