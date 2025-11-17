# Changelog

Tous les changements notables de ce projet seront documentés dans ce fichier.

Le format est basé sur [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
et ce projet adhère à [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### À venir
- Multi-région support (HA géographique)
- GraphQL API pour administration
- ML-based anomaly detection
- Support MQTT 5.0 features

---

## [1.0.0] - 2025-01-17

### ✅ Production Ready Release

Première version production-ready avec implémentation complète des PoC A (gRPC) et PoC B (MQTT).

### Added

#### PoC A - gRPC Backend
- **Serveur gRPC bidirectionnel** (.NET 8)
  - Streaming bidirectionnel persistant
  - Support 30,000 connexions simultanées
  - JWT authentication via metadata
  - Rate limiting (Token Bucket)
  - Prometheus metrics exposition (port 9090)

- **Ingestion d'événements**
  - Protobuf message format
  - Validation batches
  - Tokenization PAN (HMAC-SHA256)
  - Storage Azure Blob
  - Signature HMAC pour intégrité

- **Distribution blacklist**
  - Broadcast deltas via gRPC streams
  - Redis caching
  - Versioning (timestamp-based)

#### PoC B - MQTT Backend
- **Infrastructure EMQX**
  - Cluster EMQX 3 nodes
  - Support 30,000+ connexions
  - QoS 0/1/2 support
  - Retained messages
  - ACL per-device
  - Dashboard monitoring (port 18083)

- **Backend MQTT consumer** (.NET 8)
  - Subscribe topics: `devices/+/events`, `blacklist/delta`
  - MQTTnet client library
  - Même pipeline ingestion que gRPC
  - QoS 1 par défaut (fiabilité)

#### Infrastructure
- **Kubernetes manifests** (AKS)
  - Deployments pour gRPC et MQTT backends
  - StatefulSets pour EMQX
  - Services (LoadBalancer, ClusterIP)
  - ConfigMaps et Secrets
  - HorizontalPodAutoscalers
  - NetworkPolicies (isolation)

- **Terraform IaC**
  - AKS cluster provisioning
  - Azure Container Registry
  - Redis Cache
  - Blob Storage accounts
  - Key Vault
  - Monitoring (Log Analytics)

- **Docker Compose**
  - `docker-compose.grpc.yml` : PoC A local
  - `docker-compose.mqtt.yml` : PoC B local
  - Redis, Prometheus, Grafana inclus

#### Simulateurs
- **GrpcDeviceSimulator** (.NET 8)
  - Simulation N devices concurrents
  - JWT authentication
  - Métriques avancées (P50/P95/P99/P99.9)
  - Export CSV
  - Modes : normal, burst, staggered, stress
  - Exponential backoff reconnection

- **MqttDeviceSimulator** (.NET 8)
  - Support QoS 0/1/2
  - Retained messages
  - Métriques similaires à gRPC
  - Reconnexion automatique (MQTTnet)

#### Tests
- **Scripts d'intégration**
  - `test_grpc_flow.sh` : Flow complet PoC A
  - `test_mqtt_flow.sh` : Flow complet PoC B
  - `test_comparison.sh` : Comparaison automatisée

- **Tests de charge**
  - `load_test_runner.py` : Orchestration 100 simulateurs
  - Support 1k à 50k devices
  - Simulation réseau 4G (latence, packet loss)
  - Export JSON résultats

- **Analyse**
  - `analyze_results.py` : Génération rapports HTML
  - Graphiques comparatifs (matplotlib)
  - Métriques détaillées (latence, throughput, CPU, memory)

#### Sécurité
- **PCI-DSS Compliance**
  - Tokenization PAN (one-way HMAC-SHA256)
  - TLS 1.2+ obligatoire
  - Secrets Azure Key Vault
  - ACL par device (gRPC metadata / MQTT topics)
  - Audit logging structuré

- **Authentication**
  - JWT (RS256 signature)
  - Token expiration (24h)
  - Token revocation (Redis)
  - Device ID validation

#### Monitoring & Observabilité
- **Prometheus metrics**
  - `active_connections`
  - `messages_in_total` / `messages_out_total`
  - `message_latency_ms` (histogram)
  - `blacklist_delivery_latency_ms`
  - `errors_total` (par type)
  - `reconnects_total`

- **Grafana dashboards**
  - `grafana-dashboard.json` : Comparaison PoC A vs B
  - Latence P50/P95/P99
  - Throughput
  - Resource usage (CPU, memory)

- **Structured logging**
  - Serilog integration
  - JSON format
  - Correlation IDs
  - Log levels appropriés

#### Documentation
- **README.md** : Quick start, architecture overview
- **docs/ARCHITECTURE.md** : Design détaillé, patterns, ADR
- **docs/SECURITY.md** : PCI-DSS, tokenization, TLS, secrets
- **docs/PERFORMANCE.md** : Benchmarks, latency, throughput, sizing
- **docs/POC_COMPARISON.md** : Analyse comparative gRPC vs MQTT
- **docs/FAQ.md** : Questions fréquentes
- **tests/TESTING_GUIDE.md** : Guide complet des tests (546 lignes)
- **CONTRIBUTING.md** : Guide de contribution
- **CHANGELOG.md** : Ce fichier
- **ROADMAP.md** : Évolutions futures

#### CI/CD
- **GitHub Actions workflows**
  - Build & test (.NET)
  - Docker image build
  - Push vers ACR
  - Déploiement AKS (staging/production)
  - Security scanning

#### Scripts
- `scripts/validate-deployment.sh` : Validation santé déploiements
- `scripts/quick-start.sh` : Démarrage rapide (planned)

### Changed
- N/A (première release)

### Deprecated
- N/A

### Removed
- N/A

### Fixed
- N/A (première release sans bugs connus)

### Security
- Implémentation complète conformité PCI-DSS
- Tokenization PAN obligatoire
- TLS 1.2+ enforcement
- JWT authentication
- Secrets management (Key Vault)

---

## [0.2.0] - 2025-01-15 (Pre-release)

### Added
- Simulateurs avec authentification JWT
- Métriques avancées (percentiles)
- Scripts de test automatisés
- Documentation TESTING_GUIDE.md

### Fixed
- Problèmes de reconnexion gRPC
- QoS MQTT handling

---

## [0.1.0] - 2025-01-10 (Alpha)

### Added
- Backend gRPC basique
- Backend MQTT basique
- Simulateurs simples
- Infrastructure Kubernetes initiale
- Terraform templates

---

## Conventions

### Types de changements

- **Added** : Nouvelles fonctionnalités
- **Changed** : Modifications de fonctionnalités existantes
- **Deprecated** : Fonctionnalités bientôt supprimées
- **Removed** : Fonctionnalités supprimées
- **Fixed** : Corrections de bugs
- **Security** : Correctifs de sécurité

### Liens

- [Unreleased]: https://github.com/your-org/DeltaList/compare/v1.0.0...HEAD
- [1.0.0]: https://github.com/your-org/DeltaList/releases/tag/v1.0.0
- [0.2.0]: https://github.com/your-org/DeltaList/releases/tag/v0.2.0
- [0.1.0]: https://github.com/your-org/DeltaList/releases/tag/v0.1.0
