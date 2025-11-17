# DeltaList Docker Guide

This guide provides comprehensive instructions for building, running, and deploying DeltaList using Docker.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Image Architecture](#image-architecture)
- [Building Images](#building-images)
- [Running Locally](#running-locally)
- [Running Tests](#running-tests)
- [Deploying to Azure](#deploying-to-azure)
- [Image Sizes](#image-sizes)
- [Troubleshooting](#troubleshooting)
- [Best Practices](#best-practices)

## Prerequisites

- Docker 20.10+ installed
- Docker Compose 2.0+ installed
- 4GB+ RAM available
- 10GB+ disk space

**Optional:**
- Azure CLI (for ACR deployment)
- Make (for using Makefile shortcuts)

## Quick Start

### Using Makefile (Recommended)

```bash
# Start gRPC backend stack
make up-grpc

# Start MQTT backend stack
make up-mqtt

# Build all images
make build

# Run tests
make test

# View all available commands
make help
```

### Using Scripts

```bash
# Build all images
./scripts/docker-build.sh

# Push to registry
./scripts/docker-push.sh

# Cleanup
./scripts/docker-clean.sh
```

### Using Docker Compose Directly

```bash
# gRPC Backend
docker-compose -f docker-compose.grpc.yml up -d

# MQTT Backend
docker-compose -f docker-compose.mqtt.yml up -d

# Integration Tests
docker-compose -f docker-compose.test.yml up --abort-on-container-exit
```

## Image Architecture

### Multi-Stage Build

Both GrpcBackend and MqttBackend use optimized multi-stage builds:

```
Stage 1: base (aspnet:8.0-alpine)
  - Runtime base image
  - Non-root user creation
  - Minimal size (~100MB)

Stage 2: build (sdk:8.0-alpine)
  - Restore dependencies
  - Build project

Stage 3: publish
  - Publish optimized release

Stage 4: final
  - Copy published files
  - Switch to non-root user
  - Add health checks
```

### Security Features

- **Non-root user**: All containers run as `appuser` (UID 1000)
- **Alpine base**: Minimal attack surface
- **No secrets in images**: All secrets via environment variables
- **Health checks**: Built-in health monitoring

### Image Layers

Optimized for caching:

1. Base runtime image
2. Project files (`.csproj`) - cached if unchanged
3. NuGet restore - cached if dependencies unchanged
4. Source code
5. Build artifacts
6. Published output

## Building Images

### Build All Images

```bash
# Using Makefile
make build

# Using script
./scripts/docker-build.sh

# Manual build
docker build -f src/GrpcBackend/Dockerfile -t deltalist/grpc-backend:latest .
docker build -f src/MqttBackend/Dockerfile -t deltalist/mqtt-backend:latest .
```

### Build with Custom Configuration

```bash
# Debug build
BUILD_CONFIG=Debug make build-grpc

# Custom tag
TAG=v1.0.0 make build

# Custom registry
REGISTRY=myregistry make build
```

### Build Context

**IMPORTANT**: Always build from the repository root:

```bash
# Correct (from repo root)
cd /path/to/DeltaList
docker build -f src/GrpcBackend/Dockerfile -t grpc-backend .

# Incorrect (from subdirectory)
cd src/GrpcBackend
docker build -f Dockerfile -t grpc-backend .  # Will fail!
```

## Running Locally

### gRPC Backend Stack

```bash
# Start services
make up-grpc

# Or with docker-compose
docker-compose -f docker-compose.grpc.yml up -d

# View logs
make logs-grpc
# Or
docker-compose -f docker-compose.grpc.yml logs -f

# Stop services
make down-grpc
```

**Access Points:**
- gRPC Backend: `localhost:5001`
- Metrics: `http://localhost:9090`
- Prometheus: `http://localhost:9091`
- Grafana: `http://localhost:3000` (admin/admin)

### MQTT Backend Stack

```bash
# Start services
make up-mqtt

# Or with docker-compose
docker-compose -f docker-compose.mqtt.yml up -d

# View logs
make logs-mqtt

# Stop services
make down-mqtt
```

**Access Points:**
- MQTT Backend API: `http://localhost:8080`
- MQTT Broker: `localhost:1883`
- EMQX Dashboard: `http://localhost:18083` (admin/public)
- Metrics: `http://localhost:9090`
- Prometheus: `http://localhost:9091`
- Grafana: `http://localhost:3000` (admin/admin)

### Development Mode

For auto-rebuild on code changes:

```bash
# gRPC
make dev-grpc

# MQTT
make dev-mqtt
```

### Shell Access

```bash
# gRPC Backend
make shell-grpc
# Or
docker exec -it deltalist-grpc-backend /bin/sh

# MQTT Backend
make shell-mqtt

# Redis
make shell-redis
```

## Running Tests

### Integration Tests

```bash
# Using Makefile
make test

# Using docker-compose
docker-compose -f docker-compose.test.yml up --build --abort-on-container-exit
docker-compose -f docker-compose.test.yml down
```

The test suite includes:
- gRPC backend integration tests
- MQTT backend integration tests
- Redis connectivity tests
- EMQX broker tests

### Build Test Environment Only

```bash
make test-build
```

## Deploying to Azure

### Prerequisites

1. Install Azure CLI:
```bash
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
```

2. Login to Azure:
```bash
az login
```

3. Create Azure Container Registry (if not exists):
```bash
az acr create --resource-group myResourceGroup \
  --name myacrname --sku Standard
```

### Push to ACR

```bash
# Using Makefile
make push-acr ACR_NAME=myacrname ACR_LOGIN_SERVER=myacrname.azurecr.io

# Using script
export ACR_NAME=myacrname
export ACR_LOGIN_SERVER=myacrname.azurecr.io
./scripts/docker-push.sh

# Manual
az acr login --name myacrname
docker tag deltalist/grpc-backend:latest myacrname.azurecr.io/grpc-backend:latest
docker tag deltalist/mqtt-backend:latest myacrname.azurecr.io/mqtt-backend:latest
docker push myacrname.azurecr.io/grpc-backend:latest
docker push myacrname.azurecr.io/mqtt-backend:latest
```

### Pull from ACR

```bash
az acr login --name myacrname
docker pull myacrname.azurecr.io/grpc-backend:latest
docker pull myacrname.azurecr.io/mqtt-backend:latest
```

## Image Sizes

### Target Sizes

| Image | Target Size | Notes |
|-------|-------------|-------|
| grpc-backend | < 200 MB | ASP.NET Core 8.0 Alpine |
| mqtt-backend | < 200 MB | ASP.NET Core 8.0 Alpine |
| redis | ~50 MB | Redis 7 Alpine |
| emqx | ~400 MB | EMQX 5.3.2 |
| prometheus | ~250 MB | Prometheus Latest |
| grafana | ~350 MB | Grafana Latest |

### Check Image Sizes

```bash
# Using Makefile
make status

# Manual
docker images deltalist/*

# Specific image
docker images deltalist/grpc-backend:latest --format "{{.Size}}"
```

### Optimization Tips

1. **Use Alpine base images** - Reduces size by 50-70%
2. **Multi-stage builds** - Only runtime files in final image
3. **.dockerignore** - Exclude unnecessary files from build context
4. **Layer caching** - Order COPY commands for better caching
5. **Minimize layers** - Combine RUN commands where appropriate

## Troubleshooting

### Common Issues

#### 1. Build Context Error

**Error:**
```
COPY failed: file not found in build context
```

**Solution:**
Always build from repository root:
```bash
cd /path/to/DeltaList
docker build -f src/GrpcBackend/Dockerfile .
```

#### 2. Port Already in Use

**Error:**
```
Bind for 0.0.0.0:5001 failed: port is already allocated
```

**Solution:**
```bash
# Find process using port
sudo lsof -i :5001

# Or change port in docker-compose.yml
ports:
  - "5002:5001"
```

#### 3. Container Health Check Failing

**Check health status:**
```bash
make health

# Or
docker inspect deltalist-grpc-backend --format='{{.State.Health.Status}}'
```

**View health logs:**
```bash
docker inspect deltalist-grpc-backend --format='{{json .State.Health}}' | jq
```

#### 4. Volume Permission Issues

**Solution:**
```bash
# Remove volumes and recreate
make clean-volumes
make up-grpc
```

#### 5. Out of Disk Space

**Check disk usage:**
```bash
docker system df
```

**Clean up:**
```bash
# Interactive cleanup
make clean

# Or prune system
make prune

# Or aggressive cleanup
make prune-all
```

### Debug Container

Run container with shell access:

```bash
docker run -it --entrypoint /bin/sh deltalist/grpc-backend:latest
```

### View Container Logs

```bash
# Specific service
docker logs deltalist-grpc-backend

# Follow logs
docker logs -f deltalist-grpc-backend

# Last 100 lines
docker logs --tail 100 deltalist-grpc-backend
```

### Network Issues

Check container networking:

```bash
# List networks
docker network ls

# Inspect network
docker network inspect deltalist_deltalist

# Check container connectivity
docker exec deltalist-grpc-backend ping redis
```

## Best Practices

### Development

1. **Use development docker-compose** for local work:
   ```bash
   make dev-grpc
   ```

2. **Mount source code** for live reload (add to docker-compose):
   ```yaml
   volumes:
     - ./src/GrpcBackend:/app:ro
   ```

3. **Use health checks** to ensure services are ready

4. **Check logs regularly**:
   ```bash
   make logs-grpc
   ```

### Production

1. **Use specific tags**, not `latest`:
   ```bash
   TAG=v1.0.0 make build
   ```

2. **Scan for vulnerabilities**:
   ```bash
   docker scan deltalist/grpc-backend:latest
   ```

3. **Set resource limits** in docker-compose:
   ```yaml
   deploy:
     resources:
       limits:
         cpus: '1'
         memory: 512M
   ```

4. **Use secrets management**:
   - Azure Key Vault for production
   - Environment variables for development
   - Never hardcode secrets in images

5. **Enable logging**:
   ```yaml
   logging:
     driver: json-file
     options:
       max-size: "10m"
       max-file: "3"
   ```

### CI/CD

1. **Build in CI pipeline**:
   ```yaml
   - name: Build Docker images
     run: make build
   ```

2. **Run tests**:
   ```yaml
   - name: Run integration tests
     run: make test
   ```

3. **Push to registry**:
   ```yaml
   - name: Push to ACR
     run: make push-acr ACR_NAME=${{ secrets.ACR_NAME }}
   ```

4. **Tag releases**:
   ```bash
   TAG=$(git describe --tags) make build
   ```

### Security

1. **Run as non-root** (already configured in Dockerfiles)

2. **Keep base images updated**:
   ```bash
   docker pull mcr.microsoft.com/dotnet/aspnet:8.0-alpine
   docker pull mcr.microsoft.com/dotnet/sdk:8.0-alpine
   ```

3. **Scan for vulnerabilities**:
   ```bash
   docker scan deltalist/grpc-backend:latest
   ```

4. **Use private registry** for production images

5. **Rotate secrets regularly**

## Environment Variables

### gRPC Backend

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment name | `Development` |
| `ASPNETCORE_URLS` | Listen URLs | `http://+:5001` |
| `Backend__Redis__ConnectionString` | Redis connection | `redis:6379` |
| `Backend__Security__SecretKey` | JWT secret key | Required |
| `Backend__Security__Salt` | Password salt | Required |

### MQTT Backend

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment name | `Development` |
| `ASPNETCORE_URLS` | Listen URLs | `http://+:8080` |
| `Backend__Mqtt__BrokerHost` | MQTT broker host | `emqx` |
| `Backend__Mqtt__BrokerPort` | MQTT broker port | `1883` |
| `Backend__Redis__ConnectionString` | Redis connection | `redis:6379` |
| `Backend__Security__SecretKey` | JWT secret key | Required |
| `Backend__Security__Salt` | Password salt | Required |

## Resources

- [Docker Best Practices](https://docs.docker.com/develop/dev-best-practices/)
- [.NET Docker Documentation](https://docs.microsoft.com/en-us/dotnet/core/docker/introduction)
- [Azure Container Registry](https://docs.microsoft.com/en-us/azure/container-registry/)
- [Docker Compose Documentation](https://docs.docker.com/compose/)

## Support

For issues and questions:
- Check [Troubleshooting](#troubleshooting) section
- Review container logs: `make logs-grpc` or `make logs-mqtt`
- Check container health: `make health`
- Open an issue on GitHub
