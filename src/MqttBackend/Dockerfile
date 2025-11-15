FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 9090

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["DeltaList.sln", "."]
COPY ["src/Shared.Models/Shared.Models.csproj", "src/Shared.Models/"]
COPY ["src/MqttBackend/MqttBackend.csproj", "src/MqttBackend/"]

# Restore dependencies
RUN dotnet restore "src/MqttBackend/MqttBackend.csproj"

# Copy all source files
COPY . .

# Build
WORKDIR "/src/src/MqttBackend"
RUN dotnet build "MqttBackend.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MqttBackend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Run as non-root user
USER $APP_UID

ENTRYPOINT ["dotnet", "MqttBackend.dll"]
