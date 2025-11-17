# DeltaList Docker Scripts

This directory contains helper scripts for Docker operations.

## Available Scripts

### docker-build.sh

Builds all Docker images for the DeltaList project.

**Usage:**
```bash
./scripts/docker-build.sh
```

**Environment Variables:**
- `BUILD_CONFIG`: Build configuration (default: Release)
- `TAG`: Image tag (default: latest)
- `REGISTRY`: Registry name (default: deltalist)

**Example:**
```bash
# Debug build
BUILD_CONFIG=Debug ./scripts/docker-build.sh

# Custom tag
TAG=v1.0.0 ./scripts/docker-build.sh

# Custom registry
REGISTRY=myregistry TAG=v1.0.0 ./scripts/docker-build.sh
```

### docker-push.sh

Pushes Docker images to a registry (Docker Hub or Azure Container Registry).

**Usage:**
```bash
# Push to Docker Hub
./scripts/docker-push.sh

# Push to Azure Container Registry
export ACR_NAME=myacrname
export ACR_LOGIN_SERVER=myacrname.azurecr.io
./scripts/docker-push.sh
```

**Environment Variables:**
- `TAG`: Image tag (default: latest)
- `REGISTRY`: Registry name for Docker Hub (default: deltalist)
- `ACR_NAME`: Azure Container Registry name (optional)
- `ACR_LOGIN_SERVER`: ACR login server URL (optional)

**Prerequisites:**
- For Docker Hub: `docker login`
- For ACR: Azure CLI installed and `az login`

### docker-clean.sh

Interactive cleanup script for Docker resources.

**Usage:**
```bash
./scripts/docker-clean.sh
```

**Options:**
1. Stop and remove DeltaList containers
2. Remove DeltaList images
3. Remove DeltaList volumes (WARNING: data loss)
4. Full cleanup (containers + images + volumes)
5. Docker system prune (all unused Docker resources)

### docker-validate.sh

Validates Docker setup and builds all images.

**Usage:**
```bash
./scripts/docker-validate.sh
```

**Checks:**
- Docker and Docker Compose installation
- Docker daemon status
- Disk space availability
- Required files existence
- Dockerfile syntax
- docker-compose file validity
- Build all images and report sizes

## Quick Reference

```bash
# Build all images
./scripts/docker-build.sh

# Validate everything
./scripts/docker-validate.sh

# Push to ACR
export ACR_NAME=myacr
export ACR_LOGIN_SERVER=myacr.azurecr.io
./scripts/docker-push.sh

# Cleanup
./scripts/docker-clean.sh
```

## Making Scripts Executable

If scripts are not executable, run:

```bash
chmod +x scripts/*.sh
```

## See Also

- [Docker Guide](../docs/DOCKER_GUIDE.md) - Comprehensive Docker documentation
- [Makefile](../Makefile) - Make targets for common operations
