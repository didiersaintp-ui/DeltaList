# DeltaList - PoC Comparison: gRPC vs MQTT

🚀 **Proof of Concept pour comparer gRPC bidirectionnel streaming et MQTT (EMQX) pour 30k devices 4G**

## 📋 Sommaire

- [Vue d'ensemble](#-vue-densemble)
- [Architecture](#-architecture)
- [Composants](#-composants)
- [Installation](#-installation)
- [Déploiement](#-déploiement)
- [Tests de charge](#-tests-de-charge)
- [Métriques & Monitoring](#-métriques--monitoring)
- [Sécurité](#-sécurité)
- [Résultats comparatifs](#-résultats-comparatifs)

---

## 🎯 Vue d'ensemble

Ce projet implémente et compare **deux approches** pour gérer la communication bidirectionnelle avec 30 000 devices 4G :

### **PoC A - gRPC Bidirectional Streaming**
- Backend C# .NET 8 avec gRPC
- Streaming bidirectionnel persistant
- Hébergé sur AKS (Azure Kubernetes Service)

### **PoC B - MQTT avec EMQX**
- Broker MQTT EMQX en cluster sur AKS
- Backend C# .NET 8 intégré via MQTTnet
- QoS configurables, retained messages

### Objectifs métier
- ✅ 30k devices peuvent pousser **1 batch/minute** d'événements
- ✅ Backend peut distribuer des **deltas de blacklists** (PAN EMV tokenisés)
- ✅ Connexions **long-lived** stables et résilientes
- ✅ Conformité **PCI-DSS** (pas de PAN en clair)
- ✅ Latence **p95 < 5s** pour distribution blacklist

---

## 🏗️ Architecture

### PoC A - gRPC

```
┌─────────────┐         gRPC Stream          ┌──────────────────┐
│  Device 1   │◄──────────────────────────────┤                  │
├─────────────┤         Batch/Events          │  gRPC Backend    │
│  Device 2   │──────────────────────────────►│  (.NET 8)        │
├─────────────┤         Blacklist Deltas      │                  │
│   ...       │◄──────────────────────────────┤  - Ingestion     │
├─────────────┤                                │  - Blacklist Mgr │
│  Device 30k │                                │  - Metrics       │
└─────────────┘                                └──────────────────┘
                                                        │
                                                        ▼
                                               ┌─────────────────┐
                                               │  Redis Cache    │
                                               │  Event Store    │
                                               └─────────────────┘
```

### PoC B - MQTT

```
┌─────────────┐                               ┌──────────────────┐
│  Device 1   │◄──────┐                  ┌────┤                  │
├─────────────┤       │                  │    │  MQTT Backend    │
│  Device 2   │       │   MQTT Broker    │    │  (.NET 8)        │
├─────────────┤       │   (EMQX Cluster) │    │                  │
│   ...       │◄──────┤   3 nodes        ├────┤  - Subscribe     │
├─────────────┤       │                  │    │  - Publish       │
│  Device 30k │       │   QoS 1          │    │  - Blacklist Mgr │
└─────────────┘       │   Retained       │    └──────────────────┘
       │              └──────────────────┘
       │ Publish: devices/{id}/events
       │ Subscribe: devices/{id}/commands
       │            blacklist/delta
```

---

## 📦 Composants

### Backend Services

| Composant | Description | Tech Stack |
|-----------|-------------|------------|
| `GrpcBackend` | Serveur gRPC bidirectional streaming | .NET 8, Grpc.AspNetCore |
| `MqttBackend` | Backend intégré avec EMQX | .NET 8, MQTTnet |
| `Shared.Models` | Modèles Protobuf communs, sécurité | Protobuf, HMAC-SHA256 |

### Simulateurs

| Composant | Description | Usage |
|-----------|-------------|-------|
| `GrpcDeviceSimulator` | Simule N devices gRPC | Load testing PoC A |
| `MqttDeviceSimulator` | Simule N devices MQTT | Load testing PoC B |
| `load_test_runner.py` | Orchestrateur de tests | Tests 1k/5k/30k devices |

### Infrastructure

| Composant | Description | Outil |
|-----------|-------------|-------|
| AKS Cluster | Kubernetes managé Azure | Terraform |
| EMQX Cluster | Broker MQTT distribué | Kubernetes StatefulSet |
| Redis | Cache blacklist & state | Kubernetes Deployment |
| Prometheus | Collecte métriques | Kubernetes Operator |
| Grafana | Dashboards comparatifs | Kubernetes |

---

## 🚀 Installation

### Prérequis

- .NET 8 SDK
- Docker
- kubectl
- Azure CLI
- Terraform >= 1.5

### Build local

```bash
# Cloner le repo
git clone <repo-url>
cd DeltaList

# Restaurer et build
dotnet restore
dotnet build

# Build Docker images
docker build -f src/GrpcBackend/Dockerfile -t grpc-backend:local .
docker build -f src/MqttBackend/Dockerfile -t mqtt-backend:local .
```

### Lancer en local (Docker Compose)

```bash
# Lancer Redis + Backend gRPC
docker-compose -f docker-compose.grpc.yml up

# Lancer EMQX + Backend MQTT
docker-compose -f docker-compose.mqtt.yml up

# Lancer simulateur
dotnet run --project src/GrpcDeviceSimulator -- \
  --server localhost:5001 \
  --devices 100 \
  --duration 300
```

---

## ☁️ Déploiement

### 1. Provisionner l'infrastructure Azure (Terraform)

```bash
cd deployment/azure/terraform

# Initialiser
terraform init

# Planifier
terraform plan -out=tfplan

# Appliquer
terraform apply tfplan

# Récupérer kubeconfig
az aks get-credentials \
  --resource-group deltalist-rg \
  --name deltalist-aks
```

**Voir [deployment/azure/terraform/README.md](deployment/azure/terraform/README.md) pour les détails.**

### 2. Déployer sur AKS

#### PoC A - gRPC

```bash
# Exporter variables
export ACR_NAME="deltalistacr"
export IMAGE_TAG="latest"

# Déployer
kubectl apply -f deployment/kubernetes/grpc/namespace.yaml
kubectl apply -f deployment/kubernetes/grpc/redis.yaml
envsubst < deployment/kubernetes/grpc/deployment.yaml | kubectl apply -f -

# Vérifier
kubectl get pods -n deltalist
kubectl logs -f deployment/grpc-backend -n deltalist
```

#### PoC B - MQTT

```bash
# Déployer EMQX
kubectl apply -f deployment/kubernetes/mqtt/emqx-deployment.yaml

# Attendre que EMQX soit prêt
kubectl wait --for=condition=ready pod -l app=emqx -n deltalist --timeout=300s

# Déployer backend MQTT
envsubst < deployment/kubernetes/mqtt/mqtt-backend-deployment.yaml | kubectl apply -f -

# Vérifier
kubectl get svc -n deltalist
kubectl get pods -n deltalist
```

### 3. CI/CD avec GitHub Actions

Les pipelines automatisent :
- Build & test
- Push images vers ACR
- Déploiement sur AKS

**Voir [.github/workflows/ci-cd.yml](.github/workflows/ci-cd.yml)**

---

## 🧪 Tests de charge

### Test rapide (100 devices)

```bash
# gRPC
dotnet run --project src/GrpcDeviceSimulator -- \
  --server <GRPC_BACKEND_IP>:5001 \
  --devices 100 \
  --duration 300 \
  --batch-interval 60 \
  --events-per-batch 50

# MQTT
dotnet run --project src/MqttDeviceSimulator -- \
  --server <EMQX_IP> \
  --port 1883 \
  --devices 100 \
  --duration 300
```

### Test de charge complet (30k devices)

```bash
cd tests/load-tests

# gRPC - 30k devices, 100 simulateurs parallèles
python3 load_test_runner.py \
  --poc-type grpc \
  --server <GRPC_BACKEND_IP>:5001 \
  --total-devices 30000 \
  --simulators 100 \
  --duration 1800 \
  --batch-interval 60 \
  --events-per-batch 50

# MQTT - 30k devices
python3 load_test_runner.py \
  --poc-type mqtt \
  --server <EMQX_IP> \
  --total-devices 30000 \
  --simulators 100 \
  --duration 1800
```

### Simulation réseau 4G

```bash
# Ajouter latence et packet loss
python3 load_test_runner.py \
  --poc-type mqtt \
  --server <EMQX_IP> \
  --total-devices 5000 \
  --duration 600 \
  --latency-ms 200 \
  --packet-loss 0.01
```

**Voir [tests/load-tests/README.md](tests/load-tests/README.md) pour plus de détails.**

---

## 📊 Métriques & Monitoring

### Prometheus Metrics

Les deux backends exposent des métriques sur le port **9090** :

| Métrique | Description |
|----------|-------------|
| `active_connections` | Nombre de connexions actives |
| `messages_in_total` | Messages reçus depuis devices |
| `messages_out_total` | Messages envoyés vers devices |
| `message_latency_ms` | Latence de traitement des messages |
| `blacklist_delivery_latency_ms` | Latence distribution blacklist |
| `errors_total` | Compteur d'erreurs (par type) |
| `reconnects_total` | Nombre de reconnexions |
| `batch_size` | Taille des batches reçus |

### Grafana Dashboard

Dashboard comparatif disponible : `deployment/kubernetes/monitoring/grafana-dashboard.json`

Importer via Grafana UI ou :

```bash
# Si Grafana déployé sur AKS
kubectl port-forward svc/grafana 3000:3000 -n monitoring
# Ouvrir http://localhost:3000
```

### Accéder aux métriques

```bash
# Prometheus
kubectl port-forward svc/prometheus 9090:9090 -n monitoring

# Grafana
kubectl port-forward svc/grafana 3000:3000 -n monitoring

# EMQX Dashboard (PoC B uniquement)
kubectl port-forward svc/emqx 18083:18083 -n deltalist
# http://localhost:18083 (admin/admin_password_change_in_production)
```

---

## 🔒 Sécurité

### PCI-DSS Compliance

⚠️ **PAN jamais en clair** :
- Tokenisation HMAC-SHA256(PAN || salt)
- Clés secrètes stockées dans Azure Key Vault (production)
- Signatures HMAC pour intégrité des messages

### TLS / Authentification

- **gRPC** : TLS 1.2+, authentification JWT via metadata
- **MQTT** : TLS 1.2+, username/password ou client certificates
- ACL par device sur topics MQTT

### Secrets Kubernetes

```bash
# Créer secrets (NE PAS commiter en production !)
kubectl create secret generic deltalist-secrets \
  --namespace deltalist \
  --from-literal=redis-connection-string="redis:6379" \
  --from-literal=security-secret-key="YOUR_SECRET_KEY" \
  --from-literal=security-salt="YOUR_SALT"
```

**En production** : utiliser Azure Key Vault + CSI Driver

---

## 📈 Résultats comparatifs

### Critères de comparaison

| Critère | PoC A (gRPC) | PoC B (MQTT) | Notes |
|---------|--------------|--------------|-------|
| **Latence p50 (blacklist)** | X ms | Y ms | À mesurer |
| **Latence p95 (blacklist)** | X ms | Y ms | Objectif < 5s |
| **Throughput (msg/s)** | X k/s | Y k/s | 30k devices @ 1/min = 500/s |
| **CPU usage (30k devices)** | X vCPU | Y vCPU | À mesurer |
| **Memory usage** | X GB | Y GB | À mesurer |
| **Message loss rate** | X% | Y% | Objectif < 0.1% |
| **Reconnection handling** | Très bon | Excellent | MQTT natif |
| **Complexité implémentation** | Moyenne | Faible | EMQX clé en main |
| **Coût Azure estimé (mois)** | €X | €Y | Nodes + LB |

### Avantages / Inconvénients

#### PoC A - gRPC

**✅ Avantages**
- Contrôle total sur le protocole
- Protobuf compact et performant
- Streaming bidirectionnel natif
- Excellente intégration .NET

**❌ Inconvénients**
- Implémentation backend complexe
- Gestion reconnexions manuelle
- Load balancing HTTP/2 complexe
- Pas de retained messages natif

#### PoC B - MQTT

**✅ Avantages**
- Broker EMQX robuste et éprouvé
- QoS + retained messages natifs
- Reconnexion automatique
- Dashboard EMQX intégré
- Scalabilité éprouvée

**❌ Inconvénients**
- Composant externe (EMQX) à gérer
- Moins de contrôle sur protocole
- QoS 2 peut ajouter latence

---

## 📁 Structure du projet

```
DeltaList/
├── src/
│   ├── Shared.Models/           # Modèles Protobuf communs
│   ├── GrpcBackend/             # Backend gRPC .NET 8
│   ├── MqttBackend/             # Backend MQTT .NET 8
│   ├── GrpcDeviceSimulator/     # Simulateur gRPC
│   └── MqttDeviceSimulator/     # Simulateur MQTT
├── deployment/
│   ├── azure/terraform/         # Infrastructure as Code
│   └── kubernetes/
│       ├── grpc/                # Manifests PoC A
│       ├── mqtt/                # Manifests PoC B
│       └── monitoring/          # Prometheus, Grafana
├── tests/
│   └── load-tests/              # Scripts de test de charge
├── docs/                        # Documentation complémentaire
└── README.md                    # Ce fichier
```

---

## 🤝 Contributing

1. Créer une branche feature : `git checkout -b feature/ma-feature`
2. Commit : `git commit -m "Description"`
3. Push : `git push origin feature/ma-feature`
4. Ouvrir une Pull Request

---

## 📄 License

[À définir]

---

## 📞 Contact

Pour questions ou support :
- Email: [team@example.com]
- Slack: #deltalist-poc

---

## 🎓 Ressources

- [gRPC Documentation](https://grpc.io/docs/)
- [EMQX Documentation](https://www.emqx.io/docs/en/latest/)
- [.NET 8 gRPC Guide](https://learn.microsoft.com/en-us/aspnet/core/grpc/)
- [MQTTnet Library](https://github.com/dotnet/MQTTnet)
- [Azure AKS Best Practices](https://learn.microsoft.com/en-us/azure/aks/best-practices)

---

**🚀 Bon tests et bonne chance pour choisir la meilleure solution !**
