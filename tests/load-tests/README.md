# Load Tests

Scripts pour tester la charge des deux PoC.

## Utilisation

### Test gRPC (PoC A)

```bash
# Test avec 1000 devices
python3 load_test_runner.py \
  --poc-type grpc \
  --server localhost:5001 \
  --total-devices 1000 \
  --simulators 10 \
  --duration 300

# Test avec 5000 devices
python3 load_test_runner.py \
  --poc-type grpc \
  --server localhost:5001 \
  --total-devices 5000 \
  --simulators 20 \
  --duration 600

# Test avec 30000 devices
python3 load_test_runner.py \
  --poc-type grpc \
  --server localhost:5001 \
  --total-devices 30000 \
  --simulators 100 \
  --duration 1800
```

### Test MQTT (PoC B)

```bash
# Test avec 1000 devices
python3 load_test_runner.py \
  --poc-type mqtt \
  --server localhost:1883 \
  --total-devices 1000 \
  --simulators 10 \
  --duration 300

# Test avec simulation de réseau 4G
python3 load_test_runner.py \
  --poc-type mqtt \
  --server localhost:1883 \
  --total-devices 5000 \
  --simulators 20 \
  --duration 600 \
  --latency-ms 200 \
  --packet-loss 0.01
```

## Métriques à collecter

Les métriques sont disponibles via Prometheus:
- `http://localhost:9090/metrics` (backend)
- Les logs des simulateurs sont dans `logs/`
