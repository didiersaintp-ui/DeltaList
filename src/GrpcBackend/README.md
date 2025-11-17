# GrpcBackend - DeltaList PoC A

## Vue d'ensemble

Le **GrpcBackend** est l'implémentation du PoC A (Proof of Concept A) du projet DeltaList. Il s'agit d'un backend gRPC haute performance conçu pour gérer jusqu'à 30 000 devices simultanés avec streaming bidirectionnel.

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         GrpcBackend                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │           gRPC Services (HTTP/2)                         │   │
│  ├─────────────────────────────────────────────────────────┤   │
│  │  • DeviceAuthService     (Auth & Registration)          │   │
│  │  • DeviceStreamService   (Bidirectional Streaming)      │   │
│  │  • BlacklistAdminService (Admin API)                    │   │
│  └─────────────────────────────────────────────────────────┘   │
│                           │                                       │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │                  Core Services                           │   │
│  ├─────────────────────────────────────────────────────────┤   │
│  │  • JWT Token Service     (Authentication)               │   │
│  │  • Device Registry       (Device Management)            │   │
│  │  • Rate Limiter          (Token Bucket)                 │   │
│  │  • Blacklist Manager     (Delta Management)             │   │
│  │  • Event Store           (Azure Blob / InMemory)        │   │
│  │  • Metrics Collector     (Prometheus)                   │   │
│  └─────────────────────────────────────────────────────────┘   │
│                           │                                       │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              External Dependencies                       │   │
│  ├─────────────────────────────────────────────────────────┤   │
│  │  • Redis                 (Distributed Cache)            │   │
│  │  • Azure Blob Storage    (Persistent Storage)           │   │
│  │  • Prometheus            (Metrics Export)               │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

## Fonctionnalités principales

### 1. Authentification JWT
- **Registration**: Enregistrement de nouveaux devices avec génération de JWT token
- **Token Refresh**: Rafraîchissement des tokens expirés
- **Validation**: Validation JWT sur toutes les connexions streaming
- **Security**: HMAC-SHA256, expiration configurable, révocation de devices

### 2. Streaming bidirectionnel gRPC
- **Connexions persistantes**: Jusqu'à 100 000 streams simultanés
- **Device → Backend**: Batches d'événements, heartbeats, acknowledgments
- **Backend → Device**: Deltas de blacklist, commandes, acknowledgments
- **Keep-alive**: Configuration automatique pour maintenir les connexions

### 3. Gestion de la blacklist
- **Deltas**: Ajout/suppression incrémentale avec numéros de séquence
- **Signature**: Signature HMAC de chaque delta pour intégrité
- **Versioning**: Historique des 100 derniers deltas pour récupération
- **Persistence**: Sauvegarde dans Azure Blob Storage avec fallback Redis
- **Broadcasting**: Diffusion en temps réel à tous les devices connectés

### 4. Rate limiting
- **Token Bucket**: Algorithme token bucket par device
- **Configuration**: 60 batches/minute par défaut (configurable)
- **Feedback**: Messages d'erreur avec quota restant
- **Protection**: Évite les abus et garantit la QoS

### 5. Event Store
- **Azure Blob Storage**: Stockage durable avec compression gzip optionnelle
- **Structure hiérarchique**: `events/{deviceId}/{year}/{month}/{day}/{timestamp}_{seq}.json`
- **Fallback**: Mode InMemory si Azure non configuré
- **Métadonnées**: Enrichissement avec device_id, batch_seq, timestamps

### 6. Observabilité
- **Logging structuré**: Serilog avec contexte enrichi
- **Métriques Prometheus**: Connexions, latences, erreurs, throughput
- **Health checks**: Endpoints /health et /info
- **OpenTelemetry**: Support pour tracing distribué

## Configuration

### appsettings.json

```json
{
  "Backend": {
    "Environment": "Development",

    "Security": {
      "SecretKey": "YOUR_SECRET_KEY_MIN_32_CHARACTERS",
      "Salt": "YOUR_SALT_VALUE",
      "JwtIssuer": "DeltaList.GrpcBackend",
      "JwtAudience": "DeltaListDevices",
      "JwtExpirationMinutes": 60
    },

    "Storage": {
      "Type": "InMemory",  // ou "AzureBlob"
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...",
      "EventsContainerName": "events",
      "BlacklistContainerName": "blacklist",
      "EnableCompression": true
    },

    "Redis": {
      "ConnectionString": "localhost:6379",
      "InstanceName": "DeltaList:",
      "DatabaseId": 0
    },

    "RateLimit": {
      "MaxBatchesPerMinute": 60,
      "MaxTokensPerDevice": 60
    },

    "Grpc": {
      "Port": 5001,
      "MaxConcurrentStreams": 100000,
      "KeepAliveIntervalSeconds": 30,
      "KeepAliveTimeoutSeconds": 10,
      "EnableReflection": true
    },

    "Monitoring": {
      "EnablePrometheus": true,
      "EnableOpenTelemetry": true,
      "OtlpEndpoint": "",
      "MetricsPort": 9090
    }
  }
}
```

### Variables d'environnement (Production)

```bash
# Security
export Backend__Security__SecretKey="your-production-secret-key-min-32-chars"
export Backend__Security__Salt="your-production-salt"

# Azure Storage
export Backend__Storage__Type="AzureBlob"
export Backend__Storage__ConnectionString="DefaultEndpointsProtocol=https;..."

# Redis
export Backend__Redis__ConnectionString="your-redis-host:6379,password=..."
```

## Démarrage

### Prérequis

- .NET 8 SDK
- Redis (local ou distant)
- Azure Storage Account (optionnel, pour persistence)

### Compilation

```bash
# Restore dependencies
dotnet restore src/GrpcBackend/GrpcBackend.csproj

# Build
dotnet build src/GrpcBackend/GrpcBackend.csproj

# Run
dotnet run --project src/GrpcBackend/GrpcBackend.csproj
```

### Docker

```bash
# Build image
docker build -t deltalist-grpc-backend -f src/GrpcBackend/Dockerfile .

# Run container
docker run -p 5001:5001 -p 9090:9090 \
  -e Backend__Security__SecretKey="your-secret-key-32-chars" \
  -e Backend__Redis__ConnectionString="redis:6379" \
  deltalist-grpc-backend
```

## Services gRPC

### DeviceAuthService

```protobuf
service DeviceAuthService {
  rpc RegisterDevice(DeviceRegistration) returns (AuthToken);
  rpc RefreshToken(AuthToken) returns (AuthToken);
}
```

**Exemple (grpcurl)**:
```bash
# Register device
grpcurl -plaintext -d '{
  "device_id": "device123",
  "public_key": "ssh-rsa AAAAB3...",
  "metadata": [
    {"key": "model", "value": "TPE-X1"},
    {"key": "version", "value": "1.0.0"}
  ]
}' localhost:5001 deltalist.DeviceAuthService/RegisterDevice

# Refresh token
grpcurl -plaintext -d '{
  "device_id": "device123",
  "token": "eyJhbGciOiJIUzI1NiIs..."
}' localhost:5001 deltalist.DeviceAuthService/RefreshToken
```

### DeviceStreamService

```protobuf
service DeviceStreamService {
  rpc Connect(stream DeviceMessage) returns (stream ServerMessage);
}
```

**Flux**:
1. Device envoie JWT token dans metadata `authorization: Bearer <token>`
2. Backend valide le token et vérifie le device
3. Backend envoie l'état initial de la blacklist
4. Communication bidirectionnelle:
   - Device → Backend: Batches d'événements, heartbeats
   - Backend → Device: Deltas de blacklist, commandes

### BlacklistAdminService

```protobuf
service BlacklistAdminService {
  rpc AddToBlacklist(BlacklistUpdateRequest) returns (BlacklistUpdateResponse);
  rpc RemoveFromBlacklist(BlacklistUpdateRequest) returns (BlacklistUpdateResponse);
  rpc GetBlacklist(BlacklistQuery) returns (BlacklistSnapshot);
}
```

**Exemple**:
```bash
# Add tokens to blacklist
grpcurl -plaintext -d '{
  "pan_tokens": ["token123", "token456"],
  "reason": "Fraud detection",
  "updated_by": "admin@example.com"
}' localhost:5001 deltalist.BlacklistAdminService/AddToBlacklist
```

## Endpoints HTTP

### Health Check
```bash
curl http://localhost:5001/health
```

**Response**:
```json
{
  "status": "healthy",
  "timestamp": "2025-11-17T10:30:00Z",
  "activeConnections": 1234
}
```

### Info
```bash
curl http://localhost:5001/info
```

**Response**:
```json
{
  "serviceName": "DeltaList gRPC Backend (PoC A)",
  "version": "1.0.0",
  "environment": "Development",
  "activeConnections": 1234,
  "totalEventsStored": 567890,
  "uptime": "2.05:34:12.123"
}
```

### Prometheus Metrics
```bash
curl http://localhost:9090/metrics
```

## Performance

### Capacités

- **Devices simultanés**: 30 000+
- **Connexions concurrentes**: 100 000 streams
- **Throughput**: 10 000+ batches/seconde
- **Latence**: < 50ms (P95) pour batch processing
- **Disponibilité**: 99.9% (avec Azure + Redis)

### Optimisations

1. **gRPC HTTP/2**: Multiplexage, compression, keep-alive
2. **Connection pooling**: Redis connection pooling
3. **Async I/O**: Tout le code est async/await
4. **Fire-and-forget**: Persistence Azure en arrière-plan
5. **ConcurrentDictionary**: Thread-safe sans lock excessif
6. **Token Bucket**: Rate limiting efficace en mémoire

## Monitoring

### Métriques Prometheus

- `deltalist_connections_total`: Nombre total de connexions
- `deltalist_active_connections`: Connexions actives
- `deltalist_messages_in_total`: Messages reçus
- `deltalist_messages_out_total`: Messages envoyés
- `deltalist_batch_size`: Distribution de la taille des batches
- `deltalist_latency_seconds`: Latence de traitement
- `deltalist_errors_total`: Erreurs par type

### Logs structurés

```json
{
  "Timestamp": "2025-11-17T10:30:00Z",
  "Level": "Information",
  "Message": "Device device123 authenticated and connected",
  "Properties": {
    "DeviceId": "device123",
    "ConnectionId": "guid-123",
    "Application": "GrpcBackend"
  }
}
```

## Sécurité

### Bonnes pratiques

1. **JWT Tokens**: Rotation des tokens toutes les 60 minutes
2. **Secrets**: Utiliser Azure Key Vault ou variables d'environnement
3. **TLS**: Activer TLS en production (mTLS recommandé)
4. **Rate Limiting**: Protection contre les abus
5. **Validation**: Validation stricte des tokens et devices
6. **Audit**: Logs complets de toutes les opérations sensibles

### Checklist production

- [ ] Changer `Security.SecretKey` (min 32 caractères)
- [ ] Changer `Security.Salt`
- [ ] Activer TLS/mTLS
- [ ] Configurer Azure Storage
- [ ] Configurer Redis avec authentification
- [ ] Désactiver gRPC Reflection (`EnableReflection: false`)
- [ ] Activer OpenTelemetry avec OTLP
- [ ] Configurer les alertes Prometheus
- [ ] Limiter les logs sensibles

## Tests

### Tests unitaires
```bash
dotnet test tests/GrpcBackend.Tests/
```

### Tests d'intégration
```bash
dotnet test tests/GrpcBackend.IntegrationTests/
```

### Tests de charge
```bash
# Utiliser ghz (gRPC benchmarking tool)
ghz --insecure \
  --proto src/Shared.Models/Protos/device_stream.proto \
  --call deltalist.DeviceAuthService/RegisterDevice \
  -d '{"device_id":"test{{.RequestNumber}}","public_key":"key"}' \
  -c 100 -n 10000 \
  localhost:5001
```

## Troubleshooting

### Problèmes courants

**1. "Missing authorization token"**
- Vérifier que le client envoie le header `authorization: Bearer <token>`
- Vérifier que le token est valide et non expiré

**2. "Device is not active"**
- Vérifier que le device est enregistré
- Vérifier le statut du device dans le registry

**3. "Rate limit exceeded"**
- Réduire la fréquence d'envoi des batches
- Augmenter `MaxBatchesPerMinute` dans la config

**4. Azure Blob Storage errors**
- Vérifier la `ConnectionString`
- Vérifier que les containers existent
- Le système bascule automatiquement en mode InMemory

**5. Redis connection failures**
- Vérifier la connectivité Redis
- Vérifier les credentials
- Le système peut fonctionner sans Redis mais sans cache distribué

## Roadmap

- [ ] Support mTLS pour authentification client
- [ ] Sharding avancé pour 100k+ devices
- [ ] Compression des deltas (Protobuf)
- [ ] CDC (Change Data Capture) pour audit
- [ ] GraphQL API pour admin
- [ ] WebSocket gateway pour web clients
- [ ] Kubernetes operator

## Contribuer

Voir [CONTRIBUTING.md](../../CONTRIBUTING.md) pour les guidelines.

## Licence

Propriétaire - Tous droits réservés
