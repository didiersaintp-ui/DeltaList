# Shared.Models - Fondation Architecturale DeltaList

## Vue d'ensemble

`Shared.Models` est la bibliothèque partagée qui sert de fondation commune pour tous les composants du projet DeltaList. Elle fournit les modèles de données, interfaces, services d'authentification et définitions Protobuf utilisés par les backends gRPC et MQTT ainsi que par les simulateurs de devices.

## Architecture

```
Shared.Models/
├── Authentication/          # Services d'authentification JWT
├── Interfaces/             # Interfaces communes
├── Models/                 # Modèles de données
├── Protos/                 # Définitions Protobuf
├── Security/               # Services de sécurité (PAN tokenization)
├── Services/               # Implémentations de services
├── Configuration/          # Configuration partagée
└── Metrics/                # Métriques et monitoring
```

## Composants principaux

### 1. Authentication (JWT)

#### JwtTokenService
Service thread-safe pour la génération et validation de tokens JWT.

**Fonctionnalités:**
- Génération de tokens d'accès (access tokens)
- Génération de tokens de rafraîchissement (refresh tokens)
- Validation de tokens avec vérification d'expiration
- Protection contre les timing attacks
- Support de 30k devices simultanés

**Configuration:**
```csharp
var config = new JwtTokenConfiguration
{
    SecretKey = "votre-clé-secrète-32-caractères-minimum",
    Issuer = "DeltaList",
    Audience = "DeltaList.Devices",
    AccessTokenExpirationMinutes = 60,    // 1 heure
    RefreshTokenExpirationDays = 30       // 30 jours
};

var jwtService = new JwtTokenService(config, logger);
```

**Utilisation:**
```csharp
// Générer un token d'accès
var (accessToken, expiresAt) = jwtService.GenerateAccessToken(
    deviceId: "device-001",
    shardId: 5
);

// Valider un token
var result = jwtService.ValidateToken(token);
if (result.IsValid)
{
    Console.WriteLine($"Device: {result.DeviceId}");
}
```

#### DeviceAuthenticationService
Implémentation complète du service d'authentification des devices.

**Fonctionnalités:**
- Authentification device avec vérification de clé publique
- Validation de tokens avec cache de révocation
- Rafraîchissement de tokens
- Révocation de tokens par device
- Thread-safe pour haute concurrence

**Utilisation:**
```csharp
var authService = new DeviceAuthenticationService(
    deviceRegistry,
    jwtTokenService,
    logger
);

// Authentifier un device
var authResult = await authService.AuthenticateDeviceAsync(
    deviceId: "device-001",
    publicKey: "device-public-key"
);

if (authResult.Success)
{
    Console.WriteLine($"Token: {authResult.Token}");
    Console.WriteLine($"Refresh: {authResult.RefreshToken}");
}

// Valider un token
var isValid = await authService.ValidateTokenAsync(token);

// Révoquer les tokens d'un device
await authService.RevokeDeviceTokensAsync("device-001");
```

### 2. Interfaces

#### IDeviceRegistry
Gestion du registre des devices (enregistrement, validation, révocation).

```csharp
public interface IDeviceRegistry
{
    Task<DeviceInfo> RegisterDeviceAsync(string deviceId, string publicKey, ...);
    Task<DeviceInfo?> GetDeviceAsync(string deviceId, ...);
    Task<bool> ValidateDeviceAsync(string deviceId, ...);
    Task<bool> RevokeDeviceAsync(string deviceId, string reason, ...);
    Task<IEnumerable<DeviceInfo>> GetDevicesByShardAsync(int shardId, ...);
    // ... autres méthodes
}
```

#### IAuthenticationService
Authentification et gestion des tokens JWT.

```csharp
public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateDeviceAsync(string deviceId, string publicKey, ...);
    Task<TokenValidationResult> ValidateTokenAsync(string token, ...);
    Task<AuthenticationResult> RefreshTokenAsync(string token, ...);
    Task<bool> RevokeDeviceTokensAsync(string deviceId, ...);
}
```

#### IBlacklistManager
Gestion de la blacklist PAN avec distribution de deltas.

```csharp
public interface IBlacklistManager
{
    Task<(ulong seqNo, int devicesNotified)> AddToBlacklistAsync(...);
    Task<(ulong seqNo, int devicesNotified)> RemoveFromBlacklistAsync(...);
    Task<BlacklistState> GetBlacklistStateAsync(int shardId, ...);
    Task<IEnumerable<BlacklistDelta>> GetDeltasSinceAsync(ulong sinceSeqNo, ...);
    Task<bool> IsBlacklistedAsync(string panToken, int shardId, ...);
}
```

#### IEventStore
Stockage et récupération des événements devices.

```csharp
public interface IEventStore
{
    Task<int> StoreEventsAsync(string deviceId, IEnumerable<EventRecord> events, ...);
    Task<IEnumerable<EventRecord>> GetEventsAsync(string deviceId, long fromUtc, long toUtc, ...);
    Task<long> GetEventCountAsync(string deviceId, ...);
    Task<IEnumerable<EventRecord>> GetEventsByTransactionAsync(string transactionId, ...);
}
```

### 3. Services

#### InMemoryDeviceRegistry
Implémentation en mémoire du registre de devices pour les PoC.

**Caractéristiques:**
- Thread-safe (ConcurrentDictionary)
- Sharding automatique (10 shards par défaut)
- Support de 30k+ devices
- Pour production: remplacer par implémentation avec base de données

**Utilisation:**
```csharp
var registry = new InMemoryDeviceRegistry();

// Enregistrer un device
var device = await registry.RegisterDeviceAsync(
    deviceId: "device-001",
    publicKey: "public-key-base64",
    metadata: new Dictionary<string, string> { ["region"] = "EU" }
);

// Obtenir un device
var deviceInfo = await registry.GetDeviceAsync("device-001");

// Valider un device
var isValid = await registry.ValidateDeviceAsync("device-001");

// Statistiques
var totalDevices = await registry.GetDeviceCountAsync();
var activeDevices = await registry.GetActiveDeviceCountAsync(lastSeenMinutes: 5);
```

### 4. Security

#### PanTokenizer
Tokenization PCI-compliant des numéros PAN (Primary Account Number).

**Fonctionnalités:**
- HMAC-SHA256 pour tokenization sécurisée
- Validation de format PAN (13-19 chiffres)
- Algorithme de Luhn pour validation checksums
- Thread-safe avec protection contre timing attacks
- Masquage pour logging (first 6 + last 4)

**Utilisation:**
```csharp
var tokenizer = new PanTokenizer(
    secretKey: "clé-secrète-minimum-32-caractères",
    salt: "salt-unique-pour-ce-projet"
);

// Valider un PAN
bool isValid = tokenizer.ValidatePan("4532123456789012", performLuhnCheck: true);

// Tokenizer un PAN
string token = tokenizer.TokenizePan("4532123456789012");

// Vérifier un token
bool matches = tokenizer.VerifyToken("4532123456789012", token);

// Masquer un PAN pour logging
string masked = PanTokenizer.MaskPan("4532123456789012");
// Output: "453212******9012"

// Signer un message
string signature = tokenizer.SignMessage("message-important");
bool signatureValid = tokenizer.VerifySignature("message-important", signature);
```

### 5. Models

#### DeviceInfo
```csharp
public class DeviceInfo
{
    public string DeviceId { get; set; }
    public string PublicKey { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public string Status { get; set; }  // active, suspended, revoked
    public Dictionary<string, string> Metadata { get; set; }
    public int ShardId { get; set; }
}
```

#### EventRecord
```csharp
public class EventRecord
{
    public string Id { get; set; }
    public string DeviceId { get; set; }
    public long DeviceTimestampUtc { get; set; }
    public long ReceivedTimestampUtc { get; set; }
    public uint BatchSeq { get; set; }
    public string EventType { get; set; }
    public Dictionary<string, string> Attributes { get; set; }
    public string TransactionId { get; set; }
}
```

#### BlacklistState
```csharp
public class BlacklistState
{
    public ulong CurrentSeqNo { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public HashSet<string> PanTokens { get; set; }
    public int ShardId { get; set; }
}
```

### 6. Protobuf Definitions

#### messages.proto
Définit les messages de base pour la communication:
- `Event`: Événement individuel d'un device
- `Batch`: Batch d'événements avec signature
- `BlacklistDelta`: Delta de blacklist avec ajouts/suppressions
- `Heartbeat`, `Ack`, `Command`: Messages de contrôle
- `DeviceMessage`, `ServerMessage`: Wrappers pour streaming

#### device_stream.proto
Définit les services gRPC:
- `DeviceStreamService`: Streaming bidirectionnel
- `DeviceAuthService`: Enregistrement et authentification
- `BlacklistAdminService`: Administration de la blacklist

## Dépendances

```xml
<!-- gRPC et Protobuf -->
<PackageReference Include="Google.Protobuf" Version="3.25.1" />
<PackageReference Include="Grpc.Tools" Version="2.60.0" />

<!-- JWT Authentication -->
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="7.3.1" />
<PackageReference Include="Microsoft.IdentityModel.Tokens" Version="7.3.1" />

<!-- Logging -->
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />

<!-- Security -->
<PackageReference Include="System.Security.Cryptography.Algorithms" Version="4.3.1" />
```

## Compilation

```bash
# Restaurer les packages
dotnet restore src/Shared.Models/Shared.Models.csproj

# Compiler
dotnet build src/Shared.Models/Shared.Models.csproj

# Compiler en Release
dotnet build src/Shared.Models/Shared.Models.csproj -c Release
```

## Best Practices

### Thread-Safety
Tous les services sont conçus pour être thread-safe et supporter 30k connexions simultanées:
- `JwtTokenService`: Validation thread-safe avec caching
- `DeviceAuthenticationService`: ConcurrentDictionary pour revoked tokens
- `PanTokenizer`: Lock pour opérations critiques
- `InMemoryDeviceRegistry`: ConcurrentDictionary pour devices

### Security
- **JWT Tokens**: Expiration courte (1h) + refresh tokens (30j)
- **PAN Tokenization**: HMAC-SHA256 avec salt unique
- **Timing Attacks**: Protection via `CryptographicOperations.FixedTimeEquals`
- **Validation**: Validation stricte des entrées (PAN format, Luhn check)
- **Logging**: Masquage automatique des PANs

### Performance
- **Async/Await**: Toutes les méthodes publiques sont async
- **CancellationToken**: Support de l'annulation pour toutes les opérations
- **Sharding**: Support de sharding pour scalabilité (10 shards par défaut)
- **Caching**: Cache de tokens révoqués pour validation rapide

## Exemples d'utilisation complète

### Scénario: Enregistrement et authentification d'un device

```csharp
// 1. Setup
var registry = new InMemoryDeviceRegistry();
var jwtConfig = new JwtTokenConfiguration
{
    SecretKey = "super-secret-key-minimum-32-characters-long",
    Issuer = "DeltaList",
    Audience = "DeltaList.Devices"
};
var jwtService = new JwtTokenService(jwtConfig);
var authService = new DeviceAuthenticationService(registry, jwtService);

// 2. Enregistrer un nouveau device
var device = await registry.RegisterDeviceAsync(
    deviceId: "device-001",
    publicKey: "device-public-key-pem-format"
);
Console.WriteLine($"Device registered: {device.DeviceId}, Shard: {device.ShardId}");

// 3. Authentifier le device
var authResult = await authService.AuthenticateDeviceAsync(
    deviceId: "device-001",
    publicKey: "device-public-key-pem-format"
);

if (authResult.Success)
{
    Console.WriteLine($"Access Token: {authResult.Token}");
    Console.WriteLine($"Refresh Token: {authResult.RefreshToken}");
    Console.WriteLine($"Expires at: {authResult.ExpiresAtUtc}");
}

// 4. Valider le token lors d'une requête
var validationResult = await authService.ValidateTokenAsync(authResult.Token);
if (validationResult.IsValid)
{
    Console.WriteLine($"Token valid for device: {validationResult.DeviceId}");
}

// 5. Rafraîchir le token
var refreshResult = await authService.RefreshTokenAsync(authResult.RefreshToken);
Console.WriteLine($"New access token: {refreshResult.Token}");

// 6. Révoquer le device
await authService.RevokeDeviceTokensAsync("device-001");
```

### Scénario: Tokenization de PANs pour blacklist

```csharp
// 1. Setup du tokenizer
var tokenizer = new PanTokenizer(
    secretKey: "super-secret-key-minimum-32-characters-long",
    salt: "unique-salt-for-this-project"
);

// 2. Tokenizer plusieurs PANs
var pans = new[] { "4532123456789012", "5425233430109903" };
var tokens = new List<string>();

foreach (var pan in pans)
{
    // Valider le PAN
    if (tokenizer.ValidatePan(pan, performLuhnCheck: true))
    {
        // Tokenizer
        var token = tokenizer.TokenizePan(pan);
        tokens.Add(token);

        // Logger avec masquage
        var masked = PanTokenizer.MaskPan(pan);
        Console.WriteLine($"Tokenized PAN: {masked} -> {token}");
    }
}

// 3. Vérifier un token
bool isValid = tokenizer.VerifyToken("4532123456789012", tokens[0]);
Console.WriteLine($"Token verification: {isValid}");
```

## Migration vers Production

Pour la production, remplacer les implémentations PoC:

1. **InMemoryDeviceRegistry** → Base de données (PostgreSQL, MongoDB)
2. **Token Storage** → Redis pour cache distribué
3. **Event Store** → Time-series DB (InfluxDB, TimescaleDB)
4. **Blacklist Manager** → Service distribué avec pub/sub

## Support et Documentation

- **Architecture globale**: Voir `/docs/architecture.md`
- **PoC gRPC**: Voir `/docs/poc-a-grpc.md`
- **PoC MQTT**: Voir `/docs/poc-b-mqtt.md`
- **Sécurité PCI**: Voir `/docs/security.md`

## Auteur

Projet DeltaList - PoC Comparaison gRPC vs MQTT pour 30k devices 4G
