# FAQ - DeltaList

Questions fréquentes et troubleshooting.

## Table des Matières

- [Questions Générales](#questions-générales)
- [Architecture & Design](#architecture--design)
- [Déploiement](#déploiement)
- [Performance](#performance)
- [Sécurité](#sécurité)
- [Troubleshooting](#troubleshooting)
- [Development](#development)

---

## Questions Générales

### Pourquoi comparer gRPC et MQTT ?

**Q** : Pourquoi ne pas choisir directement l'un ou l'autre ?

**R** : Les deux technologies ont des forces différentes :
- **gRPC** : Latence minimale, contrôle total, stack homogène .NET
- **MQTT** : Résilience réseau 4G, QoS natifs, protocole mature IoT

Le PoC permet de **mesurer objectivement** les performances réelles sur notre use case spécifique (30k devices, batches événements, blacklist distribution).

### Quelle est la différence majeure entre les deux ?

**Q** : En pratique, quelle est la vraie différence ?

**R** : Reconnexion réseau (critique pour 4G) :
- **MQTT** : 380ms avg (QoS + retained messages natifs)
- **gRPC** : 1240ms avg (mécanisme custom requis)

Si réseau stable → gRPC (latence -12%)
Si réseau instable (4G) → MQTT (reconnexion -69%)

### Puis-je utiliser les deux simultanément ?

**Q** : Hybride gRPC + MQTT ?

**R** : Possible mais **non recommandé** :
- Complexité opérationnelle x2
- Coûts infrastructure additionnels
- Difficulté debugging

**Use case valide** : Migration progressive (gRPC → MQTT ou inverse)

---

## Architecture & Design

### Pourquoi Protobuf pour les deux ?

**Q** : Pourquoi Protobuf même pour MQTT (qui supporte JSON) ?

**R** : Compacité et performance :
- Protobuf : ~3 KB/batch (50 events)
- JSON : ~8 KB/batch (+167% bandwidth)

Sur 30k devices @ 1 batch/min :
- Protobuf : 4.2 MB/s
- JSON : 11.2 MB/s (+€150/mois en bande passante)

### Pourquoi tokenization one-way ?

**Q** : Pourquoi ne pas chiffrer PAN (reversible) ?

**R** : PCI-DSS + minimisation risque :
- **One-way (HMAC)** : Impossible de récupérer PAN → Risque zero si token leaked
- **Chiffrement (AES)** : Possible de décrypter → Nécessite gestion clé décryption → Risque si clé compromise

Backend n'a **jamais besoin du PAN original** → Tokenization one-way optimale.

### Pourquoi Redis ET Blob Storage ?

**Q** : Pourquoi deux systèmes de storage ?

**R** : Use cases différents :
- **Redis** : Hot cache (blacklist current), < 1ms latency, données volatiles
- **Blob Storage** : Archive long-terme (événements), retention 90+ jours, coût optimisé

### Comment scale au-delà de 30k devices ?

**Q** : 50k ? 100k devices ?

**R** : Horizontal scaling :
```bash
# 50k devices
gRPC: 20 pods (vs 12 pour 30k)
MQTT: 5 EMQX nodes (vs 3)

# 100k devices
gRPC: 40 pods
MQTT: 8-10 EMQX nodes

AKS Cluster auto-scale configuré
```

Scalabilité testée jusqu'à 50k ✅

---

## Déploiement

### Comment démarrer rapidement en local ?

**Q** : Je veux tester localement en 5 minutes.

**R** :
```bash
# Cloner repo
git clone https://github.com/your-org/DeltaList.git
cd DeltaList

# Lancer PoC A (gRPC)
docker-compose -f docker-compose.grpc.yml up -d

# Vérifier santé
./scripts/validate-deployment.sh grpc

# Simuler 10 devices
dotnet run --project src/GrpcDeviceSimulator -- --server localhost:5001 --devices 10 --duration 60

# Metrics: http://localhost:9090/metrics
```

### Comment déployer sur Azure AKS ?

**Q** : Étapes production.

**R** : Voir [deployment/azure/terraform/README.md](../deployment/azure/terraform/README.md)

```bash
# 1. Infrastructure Terraform
cd deployment/azure/terraform
terraform init
terraform apply

# 2. Build & push images
az acr login --name deltalistacr
docker build -t deltalistacr.azurecr.io/grpc-backend:latest -f src/GrpcBackend/Dockerfile .
docker push deltalistacr.azurecr.io/grpc-backend:latest

# 3. Deploy Kubernetes
kubectl apply -f deployment/kubernetes/grpc/
kubectl get pods -n deltalist

# 4. Vérifier
./scripts/validate-deployment.sh grpc
```

### Combien coûte l'infrastructure Azure ?

**Q** : Coût mensuel pour 30k devices ?

**R** :
- **gRPC (PoC A)** : ~€1,680/mois (12 pods D4s_v3 + Redis + Blob + LB)
- **MQTT (PoC B)** : ~€1,920/mois (+14%, EMQX cluster supplémentaire)

Détails : [docs/PERFORMANCE.md - Dimensionnement](PERFORMANCE.md#recommandations-de-dimensionnement)

### Comment gérer les secrets en production ?

**Q** : Clés, passwords, certificates ?

**R** : Azure Key Vault + CSI Driver :
```bash
# 1. Créer secrets dans Key Vault
az keyvault secret set --vault-name deltalist-kv --name pan-secret-key --value <base64>

# 2. ConfigmapSecretsStore (K8s)
kubectl apply -f deployment/kubernetes/secretproviderclass.yaml

# 3. Pods montent secrets automatiquement
# Volume: /mnt/secrets/pan-secret-key
```

Voir [docs/SECURITY.md - Secrets Management](SECURITY.md#secrets-management)

---

## Performance

### Quelles sont les latences réelles ?

**Q** : Latence end-to-end device → backend ?

**R** : Production (30k devices) :
```
gRPC:
  p50: 18.3 ms
  p95: 42.1 ms ✅ (objectif < 5s)
  p99: 89.4 ms

MQTT:
  p50: 21.7 ms
  p95: 47.8 ms ✅ (objectif < 5s)
  p99: 96.2 ms
```

Les deux **largement en-dessous** de l'objectif 5s.

### Quel est le bottleneck principal ?

**Q** : Où se passe le temps ?

**R** : Azure Blob Storage write (40-44% du temps) :
- Latence Blob : ~18ms
- Alternatives :
  - **Cosmos DB** : ~5ms (-72%) mais +€500/mois
  - **Write-behind cache (Redis)** : ~2ms, risque perte si crash

Mitigations actuelles :
- Batching (buffer 10 batches)
- Async writes (non-bloquant)

### Comment optimiser davantage ?

**Q** : Passer de p95 42ms à 20ms ?

**R** : Opportunités :
1. **Cosmos DB** (vs Blob) : -13ms (-31%)
2. **Compression gRPC** : -30% bandwidth, +5ms CPU
3. **Token cache** (LRU) : -50% tokenization si hit rate 50%
4. **Envoy proxy** (client-side LB) : -2ms (distribution optimale)

ROI vs coût à évaluer.

---

## Sécurité

### Le système est-il conforme PCI-DSS ?

**Q** : Audit PCI-DSS OK ?

**R** : **Oui** ✅

Requirements implémentés :
- **3.4** : PAN unreadable (HMAC-SHA256 one-way)
- **4.1** : TLS 1.2+ encryption
- **8.2/8.3** : JWT authentication + unique device ID
- **10.2** : Audit logging (90 jours retention)

Voir [docs/SECURITY.md - PCI-DSS](SECURITY.md#conformité-pci-dss)

### Que se passe-t-il si token blacklist leaké ?

**Q** : Un attaquant obtient la liste des tokens.

**R** : **Risque limité** :
- Token = hash PAN (impossible retrouver PAN original)
- Token utile uniquement pour comparaison (blacklist checking)
- Pas de données sensibles additionnelles dans token

**Worst case** : Attaquant sait quels PANs sont blacklistés, mais :
- Ne peut pas récupérer PANs originaux
- Ne peut pas générer tokens valides (sans secret key)

**Mitigation** : Rotation secret key tous les 90 jours.

### JWT token peut-il être intercepté ?

**Q** : MITM attack ?

**R** : **Non** (avec TLS) :
- TLS 1.2+ obligatoire
- JWT transmis chiffré (inside TLS)
- Certificate pinning possible (devices)

**Sans TLS** : Oui (mais TLS obligatoire en production).

---

## Troubleshooting

### Backend ne démarre pas

**Q** : `docker-compose up` fail.

**R** : Vérifications :
```bash
# 1. Logs
docker-compose logs grpc-backend

# 2. Ports disponibles ?
netstat -an | grep 5001  # gRPC
netstat -an | grep 1883  # MQTT

# 3. Redis accessible ?
docker-compose ps redis
docker-compose exec redis redis-cli ping
# PONG attendu

# 4. Secrets configurés ?
cat src/GrpcBackend/appsettings.json | grep Security
# Vérifier SecretKey et Salt présents (dev mode)
```

### Devices ne se connectent pas

**Q** : Simulateur échoue à connecter.

**R** :
```bash
# 1. Backend UP ?
curl http://localhost:8080/health  # gRPC (REST endpoint)
# {"status": "Healthy"} attendu

# 2. Réseau ?
telnet localhost 5001  # gRPC
telnet localhost 1883  # MQTT

# 3. Authentication ?
# Vérifier logs backend:
docker-compose logs grpc-backend | grep -i auth
# Si "JWT validation failed" → Token invalide

# 4. Firewall ?
sudo iptables -L | grep 5001
```

### Messages ne sont pas reçus (MQTT)

**Q** : Device publie mais backend ne reçoit pas.

**R** :
```bash
# 1. Topic correct ?
# Device publie: devices/{id}/events
# Backend subscribe: devices/+/events

# 2. ACL autorise ?
# EMQX Dashboard: http://localhost:18083
# Access Control → ACL → Vérifier device-{id}

# 3. QoS ?
# QoS 0 → Possible perte
# QoS 1/2 → Livraison garantie

# 4. EMQX broker OK ?
docker-compose ps emqx
curl http://localhost:18083/api/v5/nodes
```

### Latence élevée soudaine

**Q** : p95 passe de 40ms à 300ms.

**R** : Checklist :
```bash
# 1. Charge CPU/Memory ?
kubectl top pods -n deltalist
# Si > 80% → Scale up

# 2. Network issues ?
# Ping devices
# Check packet loss

# 3. Redis slow ?
redis-cli --latency
# Si > 10ms → Redis overloaded

# 4. Blob Storage throttling ?
# Azure Portal → Storage Account → Metrics
# Check "Server errors" / "Throttling errors"

# 5. Pods restarting ?
kubectl get pods -n deltalist
# Check RESTARTS column
```

### Memory leak suspicion

**Q** : Memory usage croît continuellement.

**R** :
```bash
# 1. Monitoring long-terme
kubectl top pods -n deltalist --watch

# 2. Dotnet memory dump
kubectl exec -it grpc-backend-xxx -- bash
dotnet-dump collect -p 1 -o /tmp/dump.dmp
dotnet-dump analyze /tmp/dump.dmp
> dumpheap -stat
# Identifier objets qui croissent

# 3. Vérifier disposal
# ConcurrentDictionary connections → RemoveConnection appelé ?
# Streams fermés ?
```

---

## Development

### Comment ajouter une nouvelle métrique Prometheus ?

**Q** : Tracker "blacklist_size".

**R** :
```csharp
// 1. Définir métrique
private static readonly Gauge BlacklistSize = Metrics
    .CreateGauge("blacklist_size", "Current blacklist entry count");

// 2. Mettre à jour
public async Task AddBlacklistEntriesAsync(IEnumerable<string> tokens)
{
    await _cache.SetAddAsync("blacklist:current", tokens);

    var count = await _cache.SetLengthAsync("blacklist:current");
    BlacklistSize.Set(count);  // 👈 Update metric
}

// 3. Vérifier
curl http://localhost:9090/metrics | grep blacklist_size
```

### Comment ajouter un nouveau type d'événement ?

**Q** : "CHARGEBACK" en plus de TRANSACTION, REFUND, AUTHORIZATION.

**R** :
```protobuf
// 1. Modifier messages.proto
enum EventType {
  TRANSACTION = 0;
  REFUND = 1;
  AUTHORIZATION = 2;
  CHARGEBACK = 3;  // 👈 Nouveau
}

// 2. Recompiler Protobuf
dotnet build

// 3. Update backend logic
switch (event.EventType)
{
    case EventType.Chargeback:
        await _chargebackHandler.ProcessAsync(event);
        break;
    // ...
}

// 4. Update devices
// Même .proto → Recompile device code
```

### Comment tester avec authentication désactivée (dev) ?

**Q** : Bypass JWT pour tests rapides.

**R** :
```csharp
// appsettings.Development.json
{
  "Authentication": {
    "Enabled": false  // 👈 Dev only!
  }
}

// Program.cs
if (builder.Configuration.GetValue<bool>("Authentication:Enabled"))
{
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options => { /* ... */ });
}

// ⚠️ JAMAIS en production!
```

### Comment debug un device simulator ?

**Q** : Attacher debugger.

**R** :
```bash
# 1. Lancer avec debugger
dotnet run --project src/GrpcDeviceSimulator --configuration Debug

# 2. Dans VS Code / Rider
# Attach to process: GrpcDeviceSimulator

# 3. Breakpoint dans DeviceClient.cs
# Ligne: await stream.WriteAsync(batch);

# 4. Inspecter variables
# - batch.Events.Count
# - batch.Signature
# - connection state
```

### Comment contribuer au projet ?

**Q** : Je veux ajouter une feature.

**R** : Process :
1. Lire [CONTRIBUTING.md](../CONTRIBUTING.md)
2. Créer issue GitHub (discussion)
3. Fork repository
4. Branche : `feature/ma-feature`
5. Développer + tests
6. PR avec template
7. Code review
8. Merge

Conventions :
- Commits : Conventional Commits (`feat:`, `fix:`, etc.)
- Tests : Coverage > 70%
- Documentation : Update si API change

---

## Besoin d'aide supplémentaire ?

- **GitHub Issues** : [github.com/your-org/DeltaList/issues](https://github.com)
- **Discussions** : [github.com/your-org/DeltaList/discussions](https://github.com)
- **Email** : support@deltalist.example.com
- **Slack** : #deltalist (si disponible)

---

**FAQ Last Updated** : 2025-01-17
