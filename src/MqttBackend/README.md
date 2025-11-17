# DeltaList MQTT Backend (PoC B)

Backend MQTT production-ready pour DeltaList, supportant 30 000 devices simultanés avec EMQX.

## Architecture

```
┌─────────────┐         MQTT (TLS)        ┌──────────┐
│   Devices   │ ◄──────────────────────► │   EMQX   │
│  (30k TPE)  │                           │  Broker  │
└─────────────┘                           └────┬─────┘
                                               │
                                          MQTT Client
                                               │
                                          ┌────▼─────┐
                                          │  MQTT    │
                                          │ Backend  │
                                          └────┬─────┘
                                               │
                        ┌──────────────────────┼────────────────┐
                        │                      │                │
                    ┌───▼───┐            ┌────▼─────┐    ┌─────▼─────┐
                    │ Redis │            │  Azure   │    │Prometheus │
                    │ Cache │            │  Blob    │    │ Metrics   │
                    └───────┘            │ Storage  │    └───────────┘
                                        └──────────┘
```

## Features

### MQTT Communication
- **Connexion bidirectionnelle** Device ↔ Backend via EMQX
- **QoS configurable** (0, 1, 2) pour fiabilité
- **Reconnexion automatique** avec exponential backoff
- **Retained messages** pour blacklist deltas (offline devices)
- **ACL granulaires** par device (topic isolation)

### Device Authentication
- **JWT tokens** ou username/password
- **Webhook authentication** vers MqttBackend
- **Device registration** via API REST
- **Device revocation** avec blacklist immédiate
- **Credentials cached** dans Redis (< 5ms auth)

### Blacklist Management
- **Delta synchronization** (incremental updates)
- **Sharding support** pour scaling horizontal
- **Catch-up mechanism** pour devices offline
- **Signature cryptographique** des deltas
- **Force sync** endpoint pour admin

### Event Storage
- **In-memory store** (PoC) → Azure Blob (production)
- **Batch ingestion** from devices
- **Event deduplication** par transaction ID
- **Time-range queries** pour analytics

### Admin REST API
- **Swagger/OpenAPI** documentation (accessible sur `/swagger`)
- **Device management** (register, revoke, list)
- **Blacklist operations** (add, remove, sync)
- **System metrics** (uptime, connections, events)
- **Health checks** for Kubernetes

## Configuration

### appsettings.json

```json
{
  "Backend": {
    "Environment": "Production",
    "Security": {
      "JwtSecretKey": "your-secure-key-min-32-chars",
      "SecretKey": "hmac-signing-key",
      "Salt": "tokenization-salt"
    },
    "Redis": {
      "ConnectionString": "redis:6379",
      "InstanceName": "DeltaList:"
    },
    "Mqtt": {
      "BrokerHost": "emqx",
      "BrokerPort": 1883,
      "ClientId": "DeltaListBackend",
      "Username": "backend",
      "Password": "secure-password",
      "UseTls": true,
      "QoS": 1,
      "RetainBlacklistDeltas": true,
      "KeepAlivePeriodSeconds": 60,
      "MaxPendingMessages": 10000
    }
  }
}
```

### Variables d'environnement (production)

```bash
Backend__Security__JwtSecretKey=...
Backend__Security__SecretKey=...
Backend__Security__Salt=...
Backend__Redis__ConnectionString=...
Backend__Mqtt__BrokerHost=emqx
Backend__Mqtt__Password=...
```

## EMQX Configuration

Configuration EMQX dans `deployment/kubernetes/mqtt/emqx-config/`:

### acl.conf - Access Control Lists
- Backend peut publier/subscribe sur tous topics
- Devices isolés à leurs propres topics
- Topic pattern: `devices/{deviceId}/events`
- Blacklist broadcast: `blacklist/delta`

### auth-http.conf - Webhook Authentication
- EMQX appelle `POST /api/auth/mqtt` pour chaque connexion
- Validation JWT ou username/password
- Cache des credentials dans Redis

### emqx.conf - Main Configuration
- 30k+ connections simultanées
- SSL/TLS configuré (port 8883)
- Retained messages enabled
- Prometheus metrics export

## API Endpoints

### Authentication
- `POST /api/auth/mqtt` - EMQX webhook authentication

### Devices
- `GET /api/devices` - List connected devices
- `POST /api/devices/register` - Register new device
- `DELETE /api/devices/{id}` - Revoke device access

### Blacklist
- `GET /api/blacklist?shardId=0` - Get current blacklist
- `POST /api/blacklist/add` - Add PAN tokens
- `POST /api/blacklist/remove` - Remove PAN tokens
- `POST /api/blacklist/sync` - Force sync to all devices

### Monitoring
- `GET /health` - Health check
- `GET /info` - Service info
- `GET /api/metrics` - Custom metrics
- `GET /metrics` - Prometheus metrics

### Documentation
- `GET /swagger` - Swagger UI (API documentation)

## Topics MQTT

### Device → Backend
- `devices/{deviceId}/events` - Event batches (Protobuf)

### Backend → Device
- `devices/{deviceId}/commands` - Commands & ACKs (Protobuf)
- `blacklist/delta` - Blacklist updates (retained, QoS 1)

## Démarrage

### Développement local

```bash
# 1. Start dependencies
docker-compose up -d redis emqx

# 2. Restore packages
dotnet restore src/MqttBackend/MqttBackend.csproj

# 3. Run backend
dotnet run --project src/MqttBackend/MqttBackend.csproj

# 4. Access Swagger
open http://localhost:5000/swagger

# 5. Access EMQX Dashboard
open http://localhost:18083
# Default: admin / public
```

### Production (Kubernetes)

```bash
# 1. Build image
docker build -t deltalist/mqtt-backend:latest -f deployment/docker/MqttBackend.Dockerfile .

# 2. Deploy to Kubernetes
kubectl apply -f deployment/kubernetes/mqtt/

# 3. Verify deployment
kubectl get pods -l app=mqtt-backend
kubectl logs -l app=mqtt-backend -f

# 4. Port forward for testing
kubectl port-forward svc/mqtt-backend 5000:80
```

## Testing

### Register a test device

```bash
curl -X POST http://localhost:5000/api/devices/register \
  -H "Content-Type: application/json" \
  -d '{
    "deviceId": "device-test-001",
    "password": "secure-password"
  }'
```

### Add tokens to blacklist

```bash
curl -X POST http://localhost:5000/api/blacklist/add \
  -H "Content-Type: application/json" \
  -d '{
    "panTokens": ["TOKEN_123", "TOKEN_456"],
    "shardId": 0,
    "reason": "Fraud detection",
    "updatedBy": "admin"
  }'
```

### Force sync to all devices

```bash
curl -X POST http://localhost:5000/api/blacklist/sync?shardId=0
```

## Performance Targets

- **Throughput**: 10,000 msgs/sec ingestion
- **Latency**: < 50ms p99 for event storage
- **Auth**: < 10ms p99 authentication
- **Availability**: 99.9% uptime
- **Scalability**: 30,000+ concurrent devices
- **Reconnection**: < 5s average reconnect time

## Monitoring

### Métriques Prometheus

- `mqtt_connections_total` - Total MQTT connections
- `mqtt_messages_received_total` - Messages received from devices
- `mqtt_messages_sent_total` - Messages sent to devices
- `blacklist_delta_published_total` - Blacklist deltas published
- `event_batches_stored_total` - Event batches stored
- `authentication_requests_total` - Auth requests (success/failure)

### Logs

```bash
# View logs
kubectl logs -l app=mqtt-backend -f

# Filter authentication
kubectl logs -l app=mqtt-backend | grep "authentication"

# Filter errors
kubectl logs -l app=mqtt-backend | grep "ERROR"
```

### EMQX Dashboard

Access EMQX dashboard at http://emqx:18083
- Monitor connected devices
- View topic subscriptions
- Check authentication failures
- Monitor message rates

## Security Checklist

- [ ] Change default passwords in appsettings.json
- [ ] Generate secure JWT secret key (min 32 chars)
- [ ] Enable TLS for MQTT (port 8883)
- [ ] Configure TLS certificates
- [ ] Enable authentication webhook
- [ ] Review ACL rules in acl.conf
- [ ] Use Redis with authentication
- [ ] Enable network policies in Kubernetes
- [ ] Set resource limits (CPU/memory)
- [ ] Configure log retention
- [ ] Enable audit logging
- [ ] Set up alerting (Prometheus/Grafana)

## Troubleshooting

### Device cannot connect

1. Check EMQX logs: `docker logs emqx | grep ERROR`
2. Verify device credentials: `curl http://localhost:5000/api/devices`
3. Test authentication: Check MqttBackend logs for auth failures
4. Verify ACL rules: Device username must be `device-{deviceId}`

### Blacklist delta not received

1. Check MQTT connection: `GET /health`
2. Verify retained messages: Check EMQX dashboard → Retained
3. Force sync: `POST /api/blacklist/sync`
4. Check device subscription: Topic `blacklist/delta`

### High memory usage

1. Check event store size: `GET /info` → `totalEventsStored`
2. Clear old events: Implement cleanup job
3. Scale horizontally: Add more backend replicas
4. Use Azure Blob for events (instead of in-memory)

## Production Deployment Notes

### Scalabilité
- Deploy multiple MqttBackend replicas (stateless)
- Use Redis Cluster for high availability
- Configure EMQX cluster (3+ nodes)
- Implement sharding for 100k+ devices

### Persistance
- Replace InMemoryEventStore with Azure Blob Storage
- Configure Redis persistence (AOF + RDB)
- Backup EMQX cluster data

### Monitoring
- Set up Grafana dashboards
- Configure alerts (PagerDuty/Opsgenie)
- Enable distributed tracing (OpenTelemetry)
- Log aggregation (Azure Log Analytics)

## License

Copyright (c) 2025 DeltaList Team
