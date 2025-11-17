#!/bin/bash

# ============================================
# DeltaList - Docker Cleanup Script
# ============================================
# This script cleans up Docker images, containers, and volumes

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}============================================${NC}"
echo -e "${BLUE}DeltaList Docker Cleanup${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""

# Function to show menu
show_menu() {
    echo -e "${YELLOW}Select cleanup option:${NC}"
    echo "  1) Stop and remove DeltaList containers"
    echo "  2) Remove DeltaList images"
    echo "  3) Remove DeltaList volumes (WARNING: data loss)"
    echo "  4) Full cleanup (containers + images + volumes)"
    echo "  5) Docker system prune (all unused Docker resources)"
    echo "  6) Exit"
    echo ""
}

# Function to stop and remove containers
cleanup_containers() {
    echo -e "${BLUE}Stopping and removing DeltaList containers...${NC}"

    # Stop docker-compose services
    if [ -f "docker-compose.grpc.yml" ]; then
        docker-compose -f docker-compose.grpc.yml down 2>/dev/null || true
    fi

    if [ -f "docker-compose.mqtt.yml" ]; then
        docker-compose -f docker-compose.mqtt.yml down 2>/dev/null || true
    fi

    if [ -f "docker-compose.test.yml" ]; then
        docker-compose -f docker-compose.test.yml down 2>/dev/null || true
    fi

    # Remove individual containers
    containers=$(docker ps -a --filter "name=deltalist-" --format "{{.ID}}" 2>/dev/null || true)
    if [ -n "$containers" ]; then
        echo "$containers" | xargs docker rm -f 2>/dev/null || true
        echo -e "${GREEN}✓ Containers removed${NC}"
    else
        echo -e "${YELLOW}No DeltaList containers found${NC}"
    fi
}

# Function to remove images
cleanup_images() {
    echo -e "${BLUE}Removing DeltaList images...${NC}"

    images=$(docker images --filter "reference=deltalist/*" --format "{{.ID}}" 2>/dev/null || true)
    if [ -n "$images" ]; then
        echo "$images" | xargs docker rmi -f 2>/dev/null || true
        echo -e "${GREEN}✓ Images removed${NC}"
    else
        echo -e "${YELLOW}No DeltaList images found${NC}"
    fi

    # Also remove tagged images
    docker images | grep -E "grpc-backend|mqtt-backend" | awk '{print $3}' | xargs docker rmi -f 2>/dev/null || true
}

# Function to remove volumes
cleanup_volumes() {
    echo -e "${RED}WARNING: This will delete all data in DeltaList volumes!${NC}"
    read -p "Are you sure? (yes/NO) " -r
    echo
    if [[ ! $REPLY =~ ^[Yy][Ee][Ss]$ ]]; then
        echo -e "${YELLOW}Volume cleanup cancelled${NC}"
        return
    fi

    echo -e "${BLUE}Removing DeltaList volumes...${NC}"

    # Remove docker-compose volumes
    if [ -f "docker-compose.grpc.yml" ]; then
        docker-compose -f docker-compose.grpc.yml down -v 2>/dev/null || true
    fi

    if [ -f "docker-compose.mqtt.yml" ]; then
        docker-compose -f docker-compose.mqtt.yml down -v 2>/dev/null || true
    fi

    if [ -f "docker-compose.test.yml" ]; then
        docker-compose -f docker-compose.test.yml down -v 2>/dev/null || true
    fi

    # Remove named volumes
    volumes=$(docker volume ls --filter "name=deltalist" --format "{{.Name}}" 2>/dev/null || true)
    if [ -n "$volumes" ]; then
        echo "$volumes" | xargs docker volume rm 2>/dev/null || true
        echo -e "${GREEN}✓ Volumes removed${NC}"
    else
        echo -e "${YELLOW}No DeltaList volumes found${NC}"
    fi
}

# Function to full cleanup
full_cleanup() {
    echo -e "${RED}WARNING: This will remove all DeltaList containers, images, and volumes!${NC}"
    read -p "Are you sure? (yes/NO) " -r
    echo
    if [[ ! $REPLY =~ ^[Yy][Ee][Ss]$ ]]; then
        echo -e "${YELLOW}Full cleanup cancelled${NC}"
        return
    fi

    cleanup_containers
    cleanup_images
    cleanup_volumes

    echo -e "${GREEN}✓ Full cleanup complete${NC}"
}

# Function to docker system prune
system_prune() {
    echo -e "${YELLOW}This will remove:${NC}"
    echo "  - All stopped containers"
    echo "  - All networks not used by at least one container"
    echo "  - All dangling images"
    echo "  - All dangling build cache"
    echo ""
    read -p "Continue? (y/N) " -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo -e "${YELLOW}System prune cancelled${NC}"
        return
    fi

    docker system prune -f
    echo -e "${GREEN}✓ System prune complete${NC}"

    # Ask about volumes
    echo ""
    read -p "Also remove all unused volumes? (y/N) " -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        docker system prune -f --volumes
        echo -e "${GREEN}✓ Volumes pruned${NC}"
    fi
}

# Main menu loop
while true; do
    show_menu
    read -p "Enter option (1-6): " choice

    case $choice in
        1)
            cleanup_containers
            ;;
        2)
            cleanup_images
            ;;
        3)
            cleanup_volumes
            ;;
        4)
            full_cleanup
            ;;
        5)
            system_prune
            ;;
        6)
            echo -e "${GREEN}Goodbye!${NC}"
            exit 0
            ;;
        *)
            echo -e "${RED}Invalid option${NC}"
            ;;
    esac

    echo ""
    echo -e "${BLUE}Press Enter to continue...${NC}"
    read
    clear
done
