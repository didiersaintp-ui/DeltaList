#!/bin/bash

# Quick Start Script for DeltaList PoC
# Usage: ./scripts/quick-start.sh [grpc|mqtt]

set -e

POC_TYPE=${1:-grpc}

echo "========================================="
echo "DeltaList Quick Start - PoC ${POC_TYPE^^}"
echo "========================================="
echo ""

# Check prerequisites
command -v docker >/dev/null 2>&1 || { echo "❌ Docker is required but not installed. Aborting." >&2; exit 1; }
command -v docker-compose >/dev/null 2>&1 || { echo "❌ Docker Compose is required but not installed. Aborting." >&2; exit 1; }
command -v dotnet >/dev/null 2>&1 || { echo "❌ .NET 8 SDK is required but not installed. Aborting." >&2; exit 1; }

echo "✅ Prerequisites check passed"
echo ""

# Start infrastructure
echo "📦 Starting infrastructure with Docker Compose..."
if [ "$POC_TYPE" == "grpc" ]; then
    docker-compose -f docker-compose.grpc.yml up -d
    echo "✅ gRPC backend started"
    echo "   - gRPC endpoint: localhost:5001"
    echo "   - Metrics: http://localhost:9090/metrics"
    echo "   - Health: http://localhost:9090/health"
elif [ "$POC_TYPE" == "mqtt" ]; then
    docker-compose -f docker-compose.mqtt.yml up -d
    echo "✅ MQTT infrastructure started"
    echo "   - MQTT broker: localhost:1883"
    echo "   - EMQX Dashboard: http://localhost:18083 (admin/public)"
    echo "   - Backend API: http://localhost:8080"
    echo "   - Metrics: http://localhost:9090/metrics"
else
    echo "❌ Invalid PoC type. Use 'grpc' or 'mqtt'"
    exit 1
fi

echo "   - Prometheus: http://localhost:9091"
echo "   - Grafana: http://localhost:3000 (admin/admin)"
echo ""

# Wait for services to be ready
echo "⏳ Waiting for services to be ready..."
sleep 10

# Check health
if [ "$POC_TYPE" == "grpc" ]; then
    echo "🔍 Checking gRPC backend health..."
    curl -s http://localhost:9090/health | jq '.' || echo "Health check endpoint not responding yet"
else
    echo "🔍 Checking MQTT backend health..."
    curl -s http://localhost:8080/health | jq '.' || echo "Health check endpoint not responding yet"
fi

echo ""
echo "========================================="
echo "🎉 Infrastructure is ready!"
echo "========================================="
echo ""
echo "Next steps:"
echo ""
echo "1. Run a simulator test:"
if [ "$POC_TYPE" == "grpc" ]; then
    echo "   dotnet run --project src/GrpcDeviceSimulator -- \\"
    echo "     --server localhost:5001 \\"
    echo "     --devices 10 \\"
    echo "     --duration 60 \\"
    echo "     --verbose"
else
    echo "   dotnet run --project src/MqttDeviceSimulator -- \\"
    echo "     --server localhost \\"
    echo "     --port 1883 \\"
    echo "     --devices 10 \\"
    echo "     --duration 60 \\"
    echo "     --verbose"
fi
echo ""
echo "2. Add items to blacklist (example):"
if [ "$POC_TYPE" == "grpc" ]; then
    echo "   # TODO: Use grpcurl to call BlacklistAdminService"
else
    echo "   curl -X POST http://localhost:8080/api/blacklist/add \\"
    echo "     -H 'Content-Type: application/json' \\"
    echo "     -d '{\"panTokens\": [\"token1\", \"token2\"], \"reason\": \"Test\", \"updatedBy\": \"admin\"}'"
fi
echo ""
echo "3. View metrics:"
echo "   - Open http://localhost:9091 (Prometheus)"
echo "   - Open http://localhost:3000 (Grafana)"
echo ""
echo "4. Stop everything:"
if [ "$POC_TYPE" == "grpc" ]; then
    echo "   docker-compose -f docker-compose.grpc.yml down"
else
    echo "   docker-compose -f docker-compose.mqtt.yml down"
fi
echo ""
echo "========================================="
