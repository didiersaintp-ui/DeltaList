FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5001
EXPOSE 9090

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["DeltaList.sln", "."]
COPY ["src/Shared.Models/Shared.Models.csproj", "src/Shared.Models/"]
COPY ["src/GrpcBackend/GrpcBackend.csproj", "src/GrpcBackend/"]

# Restore dependencies
RUN dotnet restore "src/GrpcBackend/GrpcBackend.csproj"

# Copy all source files
COPY . .

# Build
WORKDIR "/src/src/GrpcBackend"
RUN dotnet build "GrpcBackend.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GrpcBackend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Run as non-root user
USER $APP_UID

ENTRYPOINT ["dotnet", "GrpcBackend.dll"]
