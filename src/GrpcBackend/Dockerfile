# ============================================
# Stage 1: Base runtime image
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base

# Add metadata labels
LABEL maintainer="DeltaList Team"
LABEL org.opencontainers.image.title="DeltaList gRPC Backend"
LABEL org.opencontainers.image.description="High-performance gRPC backend for DeltaList IoT platform"
LABEL org.opencontainers.image.version="1.0.0"

WORKDIR /app
EXPOSE 5001
EXPOSE 9090

# Install wget for healthcheck
RUN apk add --no-cache wget

# Create non-root user
RUN addgroup -g 1000 appgroup && \
    adduser -u 1000 -G appgroup -s /bin/sh -D appuser && \
    chown -R appuser:appgroup /app

# ============================================
# Stage 2: Build (using Debian for Grpc.Tools/protoc compatibility)
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution and project files first (for better layer caching)
COPY ["DeltaList.sln", "./"]
COPY ["src/Shared.Models/Shared.Models.csproj", "src/Shared.Models/"]
COPY ["src/GrpcBackend/GrpcBackend.csproj", "src/GrpcBackend/"]

# Restore dependencies (cached if project files unchanged)
RUN dotnet restore "src/GrpcBackend/GrpcBackend.csproj"

# Copy all source files
COPY . .

# Build and publish in single step
WORKDIR "/src/src/GrpcBackend"
RUN dotnet publish "GrpcBackend.csproj" \
    -c ${BUILD_CONFIGURATION} \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ============================================
# Stage 3: Final runtime image
# ============================================
FROM base AS final

WORKDIR /app

# Copy published files from build stage
COPY --from=build --chown=appuser:appgroup /app/publish .

# Switch to non-root user
USER appuser

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:9090/health || exit 1

ENTRYPOINT ["dotnet", "GrpcBackend.dll"]
