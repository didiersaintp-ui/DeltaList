# Comparaison détaillée des PoC

Ce document fournit une analyse approfondie des deux approches.

## Table des matières

1. [Résumé Exécutif](#résumé-exécutif)
2. [Critères de décision](#critères-de-décision)
3. [Analyse technique](#analyse-technique)
4. [Coûts](#coûts)
5. [Recommandation](#recommandation)

---

## Résumé Exécutif

### PoC A - gRPC Bidirectional Streaming

**Approche** : Streaming bidirectionnel HTTP/2 natif avec Protobuf

**Forces** :
- Contrôle total du protocole
- Excellentes performances Protobuf
- Forte intégration .NET
- Pas de composant externe critique

**Faiblesses** :
- Complexité implémentation
- Gestion reconnexions manuelle
- Load balancing HTTP/2 complexe

### PoC B - MQTT avec EMQX

**Approche** : Broker MQTT distribué avec backend .NET intégré

**Forces** :
- Protocole mature et éprouvé
- EMQX robuste et scalable
- QoS + retained messages natifs
- Dashboard monitoring intégré

**Faiblesses** :
- Dépendance à EMQX
- Moins de contrôle bas niveau

---

## Critères de décision

### 1. Performance & Scalabilité

| Critère | gRPC | MQTT | Gagnant |
|---------|------|------|---------|
| Latence p50 | TBD ms | TBD ms | À mesurer |
| Latence p95 | TBD ms | TBD ms | À mesurer |
| Latence p99 | TBD ms | TBD ms | À mesurer |
| Throughput max | TBD msg/s | TBD msg/s | À mesurer |
| CPU @ 30k devices | TBD vCPU | TBD vCPU | À mesurer |
| Memory @ 30k devices | TBD GB | TBD GB | À mesurer |
| Scalabilité horizontale | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | MQTT |

**Notes** :
- EMQX prouvé à millions de connexions
- gRPC excellent si bien configuré
- Les deux atteignent les objectifs métier

### 2. Résilience & Disponibilité

| Critère | gRPC | MQTT | Gagnant |
|---------|------|------|---------|
| Reconnexion automatique | Manuel | Natif | MQTT |
| Retained messages | À implémenter | Natif | MQTT |
| QoS garanties | À implémenter | Natif (0/1/2) | MQTT |
| Tolérance panne nœud | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | MQTT |
| Message persistence | Redis custom | EMQX natif | MQTT |

**Notes** :
- MQTT conçu pour réseaux instables
- gRPC nécessite implémentation retry logic
- Les deux OK pour 4G

### 3. Facilité d'implémentation

| Critère | gRPC | MQTT | Gagnant |
|---------|------|------|---------|
| Complexité backend | Élevée | Moyenne | MQTT |
| Complexité client | Moyenne | Faible | MQTT |
| Temps de développement | ~4 semaines | ~2 semaines | MQTT |
| Courbe d'apprentissage | Moyenne | Faible | MQTT |
| Debugging | Complexe | Simple (Dashboard) | MQTT |

**Notes** :
- EMQX Dashboard facilite debug
- gRPC nécessite plus de code custom
- Les deux ont bonne doc

### 4. Opérationnalité & Monitoring

| Critère | gRPC | MQTT | Gagnant |
|---------|------|------|---------|
| Monitoring natif | Prometheus custom | EMQX Dashboard | MQTT |
| Alerting | Custom | EMQX + Custom | MQTT |
| Logs structurés | ✅ | ✅ | Égalité |
| Tracing distribué | OpenTelemetry | OpenTelemetry | Égalité |
| Ops overhead | Moyen | Élevé (EMQX) | gRPC |

**Notes** :
- EMQX ajoute un composant à maintenir
- gRPC plus simple déploiement
- Les deux OK pour AKS

### 5. Sécurité

| Critère | gRPC | MQTT | Gagnant |
|---------|------|------|---------|
| TLS 1.2+ | ✅ | ✅ | Égalité |
| Authentification | JWT/mTLS | Username/Password/mTLS | Égalité |
| ACL par device | Custom | EMQX natif | MQTT |
| PCI compliance | ✅ | ✅ | Égalité |
| Audit logs | Custom | EMQX + Custom | MQTT |

**Notes** :
- Les deux conformes PCI
- EMQX ACL plus flexibles
- gRPC contrôle total

### 6. Coûts

#### PoC A - gRPC

**Infrastructure Azure (30k devices)**
- AKS nodes: 10x Standard_D4s_v3 @ ~120€/mois = **1200€/mois**
- Load Balancer Standard: **~30€/mois**
- Redis Cache (managed): **~100€/mois**
- Log Analytics: **~50€/mois**
- **TOTAL: ~1380€/mois**

**Dev/Maintenance**
- Développement: ~4 semaines
- Maintenance: Moyen

#### PoC B - MQTT

**Infrastructure Azure (30k devices)**
- AKS nodes: 12x Standard_D4s_v3 @ ~120€/mois = **1440€/mois**
  (EMQX + Backend)
- Load Balancer Standard: **~30€/mois**
- Redis Cache: **~100€/mois**
- Log Analytics: **~50€/mois**
- **TOTAL: ~1620€/mois**

**Dev/Maintenance**
- Développement: ~2 semaines
- Maintenance: Moyen-Élevé (EMQX)

**Différence**: +240€/mois (+17%) pour MQTT

**Notes**:
- Coûts proches
- gRPC légèrement moins cher
- MQTT ROI meilleur (dev 2x plus rapide)

---

## Analyse technique

### Architecture de message

#### gRPC - Protobuf

```protobuf
message Batch {
  string device_id = 1;
  int64 batch_timestamp_utc = 2;
  uint32 batch_seq = 3;
  repeated Event events = 4;
  string signature = 5;
}
```

**Avantages**:
- Compact (~40% plus petit que JSON)
- Typage fort
- Validation schema

**Inconvénients**:
- Nécessite compilation .proto
- Debug moins facile

#### MQTT - Protobuf over MQTT

Même format Protobuf, transporté via MQTT

**Avantages**:
- Garde compacité Protobuf
- + Flexibilité MQTT (topics, QoS)

**Inconvénients**:
- Overhead header MQTT minimal

### Gestion des blacklists

#### gRPC

```csharp
// Server push delta via stream
await responseStream.WriteAsync(new ServerMessage {
    Delta = blacklistDelta
});

// Client reçoit
await foreach (var msg in stream.ReadAllAsync()) {
    if (msg.Delta != null) ApplyDelta(msg.Delta);
}
```

**Avantages**:
- Push instantané
- Pas de poll

**Inconvénients**:
- Si device offline -> perte delta
- Nécessite mécanisme de rattrapage

#### MQTT

```csharp
// Server publish avec retained flag
await mqttClient.PublishAsync(new MqttApplicationMessage {
    Topic = "blacklist/delta",
    Payload = delta.ToByteArray(),
    Retain = true,  // 👈 Clé
    QualityOfServiceLevel = AtLeastOnce
});

// Device offline reçoit au reconnect
```

**Avantages**:
- Retained message automatique
- QoS garantit livraison
- Pas de code rattrapage

**Inconvénients**:
- Stored dans EMQX

---

## Recommandation

### Scénario 1 : Time-to-market critique

**→ MQTT (PoC B)**

**Raisons**:
- Développement 2x plus rapide
- Protocole mature, moins de bugs
- EMQX Dashboard facilite debug
- Moins de risque technique

**Trade-off acceptables**:
- +17% coût infra
- Composant externe (EMQX)

### Scénario 2 : Contrôle total requis

**→ gRPC (PoC A)**

**Raisons**:
- Pas de dépendance externe critique
- Contrôle total du protocole
- Potentiel d'optimisation maximal
- Stack homogène .NET

**Trade-off acceptables**:
- Développement plus long
- Complexité accrue

### Scénario 3 : Objectif = Meilleure solution technique

**→ MQTT (PoC B)**

**Raisons**:
- Résilience supérieure (retained, QoS)
- Scalabilité prouvée (EMQX)
- Simplicité opérationnelle (Dashboard)
- Protocole conçu pour IoT

**Inconvénient mineur**:
- Composant EMQX à maintenir

---

## Recommandation finale

### ✅ MQTT (PoC B) - Recommandé

**Score global: 8/10**

**Justification**:
1. **Protocole mature** : MQTT conçu pour devices mobiles/IoT
2. **Scalabilité éprouvée** : EMQX gère millions de connexions
3. **Fonctionnalités natives** : QoS, retained, ACL
4. **Time-to-market** : Développement 2x plus rapide
5. **Résilience** : Gestion reconnexions robuste

**Quand éviter** :
- Si contrainte "pas de composant externe"
- Si stack 100% .NET requis
- Si optimisation extrême nécessaire

### 🔄 gRPC (PoC A) - Alternative valide

**Score global: 7/10**

**Justification**:
1. **Contrôle total** : Aucune dépendance externe
2. **Performance** : Protobuf très performant
3. **Stack homogène** : 100% .NET
4. **Coût légèrement inférieur** : -17%

**Quand préférer** :
- Contrainte "no external broker"
- Équipe très experte gRPC/.NET
- Optimisation ultime requise

---

## Prochaines étapes

1. **Valider avec tests de charge réels**
   - Confirmer latences
   - Mesurer CPU/Memory exact
   - Tester scénarios de panne

2. **Produire rapport final**
   - Métriques exactes
   - Graphiques comparatifs
   - Recommandation confirmée

3. **Si choix MQTT**
   - Setup EMQX en production
   - Configuration ACL devices
   - Monitoring & alerting

4. **Si choix gRPC**
   - Implémenter retry logic robuste
   - Mécanisme retained messages custom
   - Load balancer HTTP/2 optimisé
