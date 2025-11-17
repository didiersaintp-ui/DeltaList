# ============================================
# DeltaList - Makefile for Docker Operations
# ============================================

.PHONY: help build build-grpc build-mqtt test clean push up-grpc up-mqtt down-grpc down-mqtt logs-grpc logs-mqtt

# Default target
.DEFAULT_GOAL := help

# Configuration
TAG ?= latest
BUILD_CONFIG ?= Release
REGISTRY ?= deltalist

# ============================================
# Help
# ============================================
help: ## Show this help message
	@echo "DeltaList Docker Operations"
	@echo "=========================="
	@echo ""
	@echo "Available targets:"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-20s\033[0m %s\n", $$1, $$2}'

# ============================================
# Build Commands
# ============================================
build: ## Build all Docker images
	@echo "Building all images..."
	@./scripts/docker-build.sh

build-grpc: ## Build gRPC Backend image only
	@echo "Building gRPC Backend..."
	@docker build \
		-f src/GrpcBackend/Dockerfile \
		-t $(REGISTRY)/grpc-backend:$(TAG) \
		--build-arg BUILD_CONFIGURATION=$(BUILD_CONFIG) \
		.

build-mqtt: ## Build MQTT Backend image only
	@echo "Building MQTT Backend..."
	@docker build \
		-f src/MqttBackend/Dockerfile \
		-t $(REGISTRY)/mqtt-backend:$(TAG) \
		--build-arg BUILD_CONFIGURATION=$(BUILD_CONFIG) \
		.

# ============================================
# Test Commands
# ============================================
test: ## Run integration tests
	@echo "Running integration tests..."
	@docker-compose -f docker-compose.test.yml up --build --abort-on-container-exit
	@docker-compose -f docker-compose.test.yml down

test-build: ## Build test environment without running tests
	@echo "Building test environment..."
	@docker-compose -f docker-compose.test.yml build

# ============================================
# Push Commands
# ============================================
push: ## Push images to registry
	@echo "Pushing images..."
	@./scripts/docker-push.sh

push-acr: ## Push images to Azure Container Registry
	@echo "Pushing to ACR..."
	@if [ -z "$(ACR_NAME)" ] || [ -z "$(ACR_LOGIN_SERVER)" ]; then \
		echo "Error: ACR_NAME and ACR_LOGIN_SERVER must be set"; \
		echo "Usage: make push-acr ACR_NAME=myacr ACR_LOGIN_SERVER=myacr.azurecr.io"; \
		exit 1; \
	fi
	@ACR_NAME=$(ACR_NAME) ACR_LOGIN_SERVER=$(ACR_LOGIN_SERVER) ./scripts/docker-push.sh

# ============================================
# Run Commands - gRPC
# ============================================
up-grpc: ## Start gRPC stack
	@echo "Starting gRPC stack..."
	@docker-compose -f docker-compose.grpc.yml up -d
	@echo "gRPC Backend: http://localhost:5001"
	@echo "Prometheus: http://localhost:9091"
	@echo "Grafana: http://localhost:3000 (admin/admin)"

up-grpc-build: ## Build and start gRPC stack
	@echo "Building and starting gRPC stack..."
	@docker-compose -f docker-compose.grpc.yml up -d --build

down-grpc: ## Stop gRPC stack
	@echo "Stopping gRPC stack..."
	@docker-compose -f docker-compose.grpc.yml down

logs-grpc: ## Show gRPC stack logs
	@docker-compose -f docker-compose.grpc.yml logs -f

# ============================================
# Run Commands - MQTT
# ============================================
up-mqtt: ## Start MQTT stack
	@echo "Starting MQTT stack..."
	@docker-compose -f docker-compose.mqtt.yml up -d
	@echo "MQTT Backend: http://localhost:8080"
	@echo "EMQX Dashboard: http://localhost:18083 (admin/public)"
	@echo "Prometheus: http://localhost:9091"
	@echo "Grafana: http://localhost:3000 (admin/admin)"

up-mqtt-build: ## Build and start MQTT stack
	@echo "Building and starting MQTT stack..."
	@docker-compose -f docker-compose.mqtt.yml up -d --build

down-mqtt: ## Stop MQTT stack
	@echo "Stopping MQTT stack..."
	@docker-compose -f docker-compose.mqtt.yml down

logs-mqtt: ## Show MQTT stack logs
	@docker-compose -f docker-compose.mqtt.yml logs -f

# ============================================
# Cleanup Commands
# ============================================
clean: ## Run cleanup script (interactive)
	@./scripts/docker-clean.sh

clean-containers: ## Remove all DeltaList containers
	@echo "Removing containers..."
	@docker-compose -f docker-compose.grpc.yml down 2>/dev/null || true
	@docker-compose -f docker-compose.mqtt.yml down 2>/dev/null || true
	@docker-compose -f docker-compose.test.yml down 2>/dev/null || true
	@docker ps -a --filter "name=deltalist-" --format "{{.ID}}" | xargs -r docker rm -f

clean-images: ## Remove DeltaList images
	@echo "Removing images..."
	@docker images --filter "reference=deltalist/*" --format "{{.ID}}" | xargs -r docker rmi -f

clean-volumes: ## Remove DeltaList volumes (WARNING: data loss)
	@echo "WARNING: This will delete all data!"
	@read -p "Continue? (y/N) " -n 1 -r; \
	echo; \
	if [[ $$REPLY =~ ^[Yy]$$ ]]; then \
		docker-compose -f docker-compose.grpc.yml down -v 2>/dev/null || true; \
		docker-compose -f docker-compose.mqtt.yml down -v 2>/dev/null || true; \
		docker-compose -f docker-compose.test.yml down -v 2>/dev/null || true; \
		docker volume ls --filter "name=deltalist" --format "{{.Name}}" | xargs -r docker volume rm; \
	fi

# ============================================
# Status Commands
# ============================================
status: ## Show running containers and images
	@echo "DeltaList Containers:"
	@docker ps -a --filter "name=deltalist-" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
	@echo ""
	@echo "DeltaList Images:"
	@docker images --filter "reference=deltalist/*" --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}"

health: ## Check health of running containers
	@echo "Health Status:"
	@docker ps --filter "name=deltalist-" --format "{{.Names}}" | while read name; do \
		status=$$(docker inspect --format='{{.State.Health.Status}}' $$name 2>/dev/null || echo "no healthcheck"); \
		echo "  $$name: $$status"; \
	done

# ============================================
# Development Commands
# ============================================
dev-grpc: ## Start gRPC in development mode with auto-rebuild
	@docker-compose -f docker-compose.grpc.yml up --build

dev-mqtt: ## Start MQTT in development mode with auto-rebuild
	@docker-compose -f docker-compose.mqtt.yml up --build

shell-grpc: ## Open shell in gRPC backend container
	@docker exec -it deltalist-grpc-backend /bin/sh

shell-mqtt: ## Open shell in MQTT backend container
	@docker exec -it deltalist-mqtt-backend /bin/sh

shell-redis: ## Open Redis CLI
	@docker exec -it deltalist-redis redis-cli

# ============================================
# Utility Commands
# ============================================
prune: ## Prune unused Docker resources
	@echo "Pruning Docker system..."
	@docker system prune -f

prune-all: ## Prune all unused Docker resources including volumes
	@echo "WARNING: This will remove all unused volumes!"
	@read -p "Continue? (y/N) " -n 1 -r; \
	echo; \
	if [[ $$REPLY =~ ^[Yy]$$ ]]; then \
		docker system prune -af --volumes; \
	fi
