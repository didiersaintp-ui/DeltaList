# DeltaList Testing Guide

Guide complet pour tester et valider les PoC A (gRPC) et PoC B (MQTT).

## Table des matières

1. [Vue d'ensemble](#vue-densemble)
2. [Prérequis](#prérequis)
3. [Tests d'intégration](#tests-dintégration)
4. [Tests de charge](#tests-de-charge)
5. [Validation de déploiement](#validation-de-déploiement)
6. [Analyse des résultats](#analyse-des-résultats)
7. [Métriques importantes](#métriques-importantes)
8. [Troubleshooting](#troubleshooting)

## Vue d'ensemble

Ce guide couvre:
- **Tests d'intégration**: Validation du flow complet (simulateur → backend → blacklist)
- **Tests de charge**: Simulation de milliers de devices simultanés
- **Tests de comparaison**: Comparaison des performances gRPC vs MQTT
- **Validation**: Vérification du bon fonctionnement d'un déploiement

## Prérequis

### Logiciels requis

```bash
# .NET 8 SDK
dotnet --version  # >= 8.0.0

# Docker et Docker Compose
docker --version
docker-compose --version

# Python 3 (pour l'analyse)
python3 --version  # >= 3.8
pip3 install matplotlib  # Pour la génération de graphiques

# Outils optionnels mais recommandés
grpcurl --version  # Pour tester les endpoints gRPC
mosquitto_pub --version  # Pour tester MQTT
jq --version  # Pour parser JSON
```

### Compilation des simulateurs

```bash
# Compiler GrpcDeviceSimulator
dotnet build src/GrpcDeviceSimulator/GrpcDeviceSimulator.csproj

# Compiler MqttDeviceSimulator
dotnet build src/MqttDeviceSimulator/MqttDeviceSimulator.csproj
```

## Tests d'intégration

### Test gRPC complet

```bash
cd tests/integration-tests
./test_grpc_flow.sh
```

Ce script:
1. Démarre le backend gRPC et Redis
2. Lance un simulateur avec 5 devices
3. Ajoute des tokens à la blacklist
4. Vérifie la réception des events
5. Valide les métriques
6. Exporte les résultats

**Résultats attendus:**
- Backend healthy
- Connexions actives ≥ 1
- Events reçus > 0
- Fichiers de métriques générés dans `metrics/`

### Test MQTT complet

```bash
cd tests/integration-tests
./test_mqtt_flow.sh
```

Ce script:
1. Démarre EMQX, backend MQTT et Redis
2. Lance un simulateur avec 5 devices (QoS 1)
3. Publie un delta de blacklist
4. Vérifie la réception des events
5. Valide les métriques
6. Exporte les résultats

**Résultats attendus:**
- EMQX et backend healthy
- Devices connectés ≥ 1
- Events traités > 0
- QoS 1 garantit la livraison

### Test de comparaison

```bash
cd tests/integration-tests
./test_comparison.sh
```

Lance les deux PoC avec les mêmes paramètres et compare:
- Throughput
- Latence (P50, P95, P99)
- Taux de succès
- Utilisation ressources

**Configuration via variables d'environnement:**
```bash
TEST_DURATION=120 \
NUM_DEVICES=10 \
BATCH_INTERVAL=20 \
./test_comparison.sh
```

## Tests de charge

### Test de charge basique

```bash
# Test gRPC avec 100 devices
python3 tests/load-tests/load_test_runner.py \
    --poc-type grpc \
    --server localhost:5001 \
    --total-devices 100 \
    --simulators 5 \
    --duration 300 \
    --batch-interval 60 \
    --events-per-batch 50

# Test MQTT avec 100 devices
python3 tests/load-tests/load_test_runner.py \
    --poc-type mqtt \
    --server localhost:1883 \
    --total-devices 100 \
    --simulators 5 \
    --duration 300 \
    --batch-interval 60 \
    --events-per-batch 50
```

### Test de charge avec simulation réseau 4G

```bash
# Simule latence 4G (50-200ms) et perte de paquets (1%)
python3 tests/load-tests/load_test_runner.py \
    --poc-type grpc \
    --server localhost:5001 \
    --total-devices 1000 \
    --simulators 10 \
    --duration 600 \
    --latency-ms 200 \
    --packet-loss 0.01
```

### Modes de test avancés

#### Mode Burst (tous les devices démarrent simultanément)

```bash
dotnet run --project src/GrpcDeviceSimulator/GrpcDeviceSimulator.csproj -- \
    --server localhost:5001 \
    --devices 50 \
    --duration 120 \
    --test-mode burst \
    --export-metrics
```

#### Mode Staggered (démarrage progressif)

```bash
dotnet run --project src/GrpcDeviceSimulator/GrpcDeviceSimulator.csproj -- \
    --server localhost:5001 \
    --devices 100 \
    --duration 300 \
    --test-mode staggered \
    --stagger-delay-ms 1000 \
    --export-metrics
```

#### Mode Stress (charge maximale)

```bash
dotnet run --project src/GrpcDeviceSimulator/GrpcDeviceSimulator.csproj -- \
    --server localhost:5001 \
    --devices 500 \
    --duration 300 \
    --test-mode stress \
    --batch-interval 10 \
    --export-metrics
```

### Test QoS MQTT

```bash
# QoS 0 (At most once) - Pas de garantie
dotnet run --project src/MqttDeviceSimulator/MqttDeviceSimulator.csproj -- \
    --server localhost \
    --devices 50 \
    --qos 0 \
    --export-metrics

# QoS 1 (At least once) - Garantie de livraison
dotnet run --project src/MqttDeviceSimulator/MqttDeviceSimulator.csproj -- \
    --server localhost \
    --devices 50 \
    --qos 1 \
    --export-metrics

# QoS 2 (Exactly once) - Garantie exacte
dotnet run --project src/MqttDeviceSimulator/MqttDeviceSimulator.csproj -- \
    --server localhost \
    --devices 50 \
    --qos 2 \
    --export-metrics
```

## Validation de déploiement

Avant de lancer des tests, validez votre déploiement:

```bash
# Valider gRPC
./scripts/validate-deployment.sh grpc

# Valider MQTT
./scripts/validate-deployment.sh mqtt

# Mode verbose pour plus de détails
VERBOSE=true ./scripts/validate-deployment.sh grpc
```

**Checks effectués:**
- ✓ Containers Docker démarrés
- ✓ Health endpoints répondent
- ✓ Ports en écoute
- ✓ Métriques disponibles
- ✓ Test de connexion device

## Analyse des résultats

### Métriques exportées

Après un test avec `--export-metrics`, vous obtenez:

```
metrics/
├── grpc_metrics_20250117_143052.csv      # Métriques agrégées
└── grpc_latencies_20250117_143052.csv    # Latences brutes
```

### Générer un rapport de comparaison

```bash
python3 tests/load-tests/analyze_results.py \
    --grpc-dir test_results/grpc \
    --mqtt-dir test_results/mqtt \
    --output comparison_report.html \
    --charts
```

Génère:
- **comparison_report.html**: Rapport HTML interactif
- **charts/**: Graphiques de comparaison (latence, throughput, etc.)

### Interpréter le CSV de métriques

```csv
Metric,Value
Timestamp,2025-01-17 14:30:52
Uptime (seconds),120.45

# Latency Statistics
Latency Count,240
Latency Min (ms),5.23
Latency Max (ms),156.78
Latency Mean (ms),23.45
Latency P50 (ms),20.12
Latency P95 (ms),45.67
Latency P99 (ms),89.34
Latency P99.9 (ms),134.56

# Counters
batches_sent,240
events_sent,12000
blacklist_deltas_received,5
reconnects,2
requests_failed,3
requests_total,243

# Derived Metrics
Success Rate (%),98.77
Throughput (batches/sec),1.99
Event Throughput (events/sec),99.63
```

## Métriques importantes

### Latence

| Métrique | Description | Objectif |
|----------|-------------|----------|
| **P50 (médiane)** | 50% des requêtes | < 50ms |
| **P95** | 95% des requêtes | < 100ms |
| **P99** | 99% des requêtes | < 200ms |
| **P99.9** | 99.9% des requêtes | < 500ms |

### Throughput

| Métrique | Description | Objectif |
|----------|-------------|----------|
| **Batches/sec** | Débit de batches | > 10/sec/device |
| **Events/sec** | Débit d'events | > 500/sec/device |

### Fiabilité

| Métrique | Description | Objectif |
|----------|-------------|----------|
| **Success Rate** | Taux de succès | > 99% |
| **Reconnects** | Reconnexions | < 5% |
| **Messages Lost** | Messages perdus (QoS 0) | < 1% |

### Endpoints de monitoring

**gRPC Backend:**
```bash
# Health check
curl http://localhost:9090/health

# Info + métriques business
curl http://localhost:9090/info | jq .

# Métriques Prometheus
curl http://localhost:9090/metrics
```

**MQTT Backend:**
```bash
# Health check
curl http://localhost:8080/health

# Info
curl http://localhost:8080/info | jq .

# Métriques Prometheus
curl http://localhost:8081/metrics
```

**EMQX:**
```bash
# Dashboard
open http://localhost:18083
# Login: admin / public

# API Stats
curl -u admin:public http://localhost:18083/api/v5/stats
```

## Troubleshooting

### Problème: Backend ne démarre pas

**Vérifications:**
```bash
# Logs du backend
docker-compose -f docker-compose.grpc.yml logs grpc-backend

# Vérifier Redis
docker-compose -f docker-compose.grpc.yml logs redis
docker exec -it deltalist-redis-1 redis-cli ping
```

**Solutions courantes:**
- Port déjà utilisé: `lsof -i :5001`
- Redis non disponible: vérifier la connexion
- Problème de configuration: vérifier `appsettings.json`

### Problème: Devices ne se connectent pas

**Vérifications:**
```bash
# Test connexion réseau
nc -zv localhost 5001  # gRPC
nc -zv localhost 1883  # MQTT

# Vérifier firewall
sudo ufw status

# Test avec curl/grpcurl
grpcurl -plaintext localhost:5001 list
```

**Solutions:**
- Vérifier l'adresse du serveur dans les paramètres
- Désactiver temporairement le firewall
- Vérifier les logs du simulateur

### Problème: Métriques manquantes

**Vérifications:**
```bash
# Créer le répertoire manuellement
mkdir -p metrics

# Vérifier permissions
ls -la metrics/

# Lancer avec export explicite
dotnet run [...] --export-metrics --metrics-dir ./metrics
```

### Problème: QoS MQTT ne fonctionne pas comme attendu

**Vérifications:**
```bash
# Vérifier la config EMQX
docker exec -it deltalist-emqx-1 emqx_ctl status

# Tester pub/sub manuellement
mosquitto_sub -h localhost -t "test" -q 1 -v &
mosquitto_pub -h localhost -t "test" -q 1 -m "test"
```

**Comportements QoS:**
- **QoS 0**: Peut perdre des messages sous charge
- **QoS 1**: Peut dupliquer, mais garantit la livraison
- **QoS 2**: Plus lent mais exactement une fois

### Problème: Performance dégradée

**Optimisations:**

1. **Augmenter les limites système:**
```bash
# Linux
ulimit -n 65536

# Docker
# Modifier docker-compose.yml
ulimits:
  nofile:
    soft: 65536
    hard: 65536
```

2. **Ajuster les paramètres des simulateurs:**
```bash
# Réduire la fréquence d'envoi
--batch-interval 120

# Réduire le nombre d'events par batch
--events-per-batch 25

# Utiliser le mode staggered
--test-mode staggered
```

3. **Monitorer les ressources:**
```bash
# CPU/RAM
docker stats

# Connections réseau
netstat -an | grep ESTABLISHED | wc -l
```

## Exemples de scénarios complets

### Scénario 1: Test de montée en charge progressive

```bash
#!/bin/bash
# Test avec 10, 50, 100, 500, 1000 devices

for num_devices in 10 50 100 500 1000; do
    echo "Testing with $num_devices devices..."

    python3 tests/load-tests/load_test_runner.py \
        --poc-type grpc \
        --server localhost:5001 \
        --total-devices $num_devices \
        --simulators 10 \
        --duration 120

    sleep 30  # Pause entre les tests
done
```

### Scénario 2: Test d'endurance

```bash
# Test sur 24h avec conditions réalistes
dotnet run --project src/GrpcDeviceSimulator/GrpcDeviceSimulator.csproj -- \
    --server localhost:5001 \
    --devices 100 \
    --duration 86400 \
    --batch-interval 60 \
    --latency-ms 100 \
    --packet-loss 0.001 \
    --reconnect-rate 0.01 \
    --export-metrics
```

### Scénario 3: Comparaison QoS MQTT

```bash
# Test QoS 0, 1, 2 en séquence
for qos in 0 1 2; do
    echo "Testing QoS $qos..."

    dotnet run --project src/MqttDeviceSimulator/MqttDeviceSimulator.csproj -- \
        --server localhost \
        --devices 50 \
        --duration 300 \
        --qos $qos \
        --export-metrics \
        --metrics-dir "metrics/qos_$qos"

    sleep 10
done

# Analyser les résultats
python3 tests/load-tests/analyze_qos_results.py
```

## Ressources supplémentaires

- **Documentation gRPC**: https://grpc.io/docs/
- **Documentation MQTT**: https://mqtt.org/
- **EMQX Documentation**: https://www.emqx.io/docs/
- **Prometheus Metrics**: https://prometheus.io/docs/

## Support

En cas de problème:
1. Consultez les logs: `docker-compose logs`
2. Vérifiez les métriques: `curl http://localhost:9090/metrics`
3. Validez le déploiement: `./scripts/validate-deployment.sh`
4. Consultez ce guide pour le troubleshooting

Pour des tests spécifiques ou des questions, référez-vous au README.md principal du projet.
