# Security Documentation - DeltaList

## Table des Matières

1. [Vue d'ensemble sécurité](#vue-densemble-sécurité)
2. [Conformité PCI-DSS](#conformité-pci-dss)
3. [Tokenization PAN](#tokenization-pan)
4. [Authentification et Autorisation](#authentification-et-autorisation)
5. [Transport Layer Security (TLS)](#transport-layer-security-tls)
6. [Secrets Management](#secrets-management)
7. [ACL et Isolation](#acl-et-isolation)
8. [Audit Logging](#audit-logging)
9. [Security Checklist](#security-checklist)
10. [Vulnerability Reporting](#vulnerability-reporting)

---

## Vue d'ensemble sécurité

DeltaList traite des données sensibles (PAN - Primary Account Number) et doit respecter les standards de sécurité les plus stricts.

### Principes de sécurité

| Principe | Implémentation | Status |
|----------|----------------|--------|
| **Defense in depth** | Multiples couches de sécurité | ✅ Implémenté |
| **Least privilege** | ACL strictes par device | ✅ Implémenté |
| **Zero trust** | Validation à chaque couche | ✅ Implémenté |
| **Data minimization** | PAN jamais en clair | ✅ Implémenté |
| **Audit trail** | Logging complet | ✅ Implémenté |

### Threat Model

```
┌─────────────────────────────────────────────────────────────────┐
│                         Threat Model                            │
└─────────────────────────────────────────────────────────────────┘

Threats identifiés:
1. ⚠️  Interception réseau (MITM)
   → Mitigation: TLS 1.2+ obligatoire

2. ⚠️  Exposition PAN en clair
   → Mitigation: Tokenization one-way (HMAC-SHA256)

3. ⚠️  Device impersonation
   → Mitigation: JWT authentication + device_id validation

4. ⚠️  Unauthorized access to topics/streams
   → Mitigation: ACL strictes (gRPC metadata / MQTT ACL)

5. ⚠️  Replay attacks
   → Mitigation: Batch sequence numbers + timestamps

6. ⚠️  Secrets exposure
   → Mitigation: Azure Key Vault + Kubernetes secrets

7. ⚠️  Injection attacks
   → Mitigation: Protobuf (typage fort) + validation

8. ⚠️  DDoS
   → Mitigation: Rate limiting + Azure DDoS Protection
```

---

## Conformité PCI-DSS

### Exigences PCI-DSS applicables

DeltaList respecte les exigences PCI-DSS suivantes :

#### Requirement 3: Protect Stored Cardholder Data

**3.4 - Render PAN unreadable**

✅ **Implémentation** :
```csharp
// PAN JAMAIS stocké en clair
// Tokenization one-way via HMAC-SHA256

public class PANTokenizer : IPANTokenizer
{
    private readonly byte[] _secretKey;  // 256 bits, stocké dans Key Vault
    private readonly byte[] _salt;       // 128 bits, unique par environnement

    public string TokenizePAN(string plainPAN)
    {
        // ⚠️ plainPAN existe uniquement en mémoire, jamais loggé

        // 1. Validation format PAN (13-19 digits)
        if (!Regex.IsMatch(plainPAN, @"^\d{13,19}$"))
        {
            throw new SecurityException("Invalid PAN format");
        }

        // 2. Combine PAN + salt
        var combined = Encoding.UTF8.GetBytes(plainPAN + Convert.ToBase64String(_salt));

        // 3. HMAC-SHA256 (one-way hash)
        using var hmac = new HMACSHA256(_secretKey);
        var hash = hmac.ComputeHash(combined);

        // 4. Token = Base64(hash) - 44 caractères
        var token = Convert.ToBase64String(hash);

        // 5. ⚠️ CRITIQUE: Clear plainPAN from memory
        Array.Clear(combined, 0, combined.Length);
        // Note: GC ne garantit pas l'effacement immédiat de plainPAN string

        return token;
    }

    // ⚠️ Détokenization IMPOSSIBLE par design
    // Conformité: PAN original jamais récupérable
}
```

**Propriétés du token** :
- Longueur fixe : 44 caractères (Base64 de 256 bits)
- Déterministe : même PAN → même token (avec même clé/salt)
- One-way : impossible de retrouver PAN à partir du token
- Collision résistant : HMAC-SHA256 cryptographiquement sûr

#### Requirement 4: Encrypt Transmission

✅ **Implémentation** :
- TLS 1.2+ obligatoire (gRPC et MQTT)
- Perfect Forward Secrecy (PFS)
- Cipher suites modernes uniquement

#### Requirement 8: Identify and Authenticate Access

✅ **Implémentation** :
- JWT authentication pour devices
- Device ID unique dans claims
- Token expiration (24h max)

#### Requirement 10: Track and Monitor Access

✅ **Implémentation** :
- Audit logging complet
- Correlation ID par requête
- Retention logs : 90 jours minimum

### PCI-DSS Compliance Matrix

| Exigence | Description | Implémentation DeltaList | Status |
|----------|-------------|--------------------------|--------|
| **3.2** | Do not store sensitive authentication data after authorization | PAN tokenisé immédiatement | ✅ |
| **3.4** | Render PAN unreadable | HMAC-SHA256 one-way | ✅ |
| **3.5** | Document key management | Voir [Secrets Management](#secrets-management) | ✅ |
| **3.6** | Key management procedures | Azure Key Vault rotation | ✅ |
| **4.1** | Use strong cryptography for transmission | TLS 1.2+ | ✅ |
| **4.2** | Never send unencrypted PANs | PAN tokenisé côté device | ✅ |
| **8.2** | Assign unique ID | Device ID unique | ✅ |
| **8.3** | Secure authentication | JWT + TLS | ✅ |
| **10.2** | Audit trail for access | Structured logging | ✅ |

---

## Tokenization PAN

### Process détaillé

```
┌─────────────────────────────────────────────────────────────────┐
│                    Tokenization Flow                            │
└─────────────────────────────────────────────────────────────────┘

Device Side (Pre-transmission):
┌──────────────┐
│ Transaction  │
│ PAN: 1234... │ ⚠️ PAN en clair (mémoire device uniquement)
└──────┬───────┘
       │
       │ (1) Load secrets from secure storage
       │     - Secret Key (256 bits)
       │     - Salt (128 bits)
       │
       ▼
┌──────────────────────────┐
│ Tokenize PAN             │
│ token = HMAC-SHA256(     │
│   PAN + salt,            │
│   secret_key             │
│ )                        │
└──────────────────────────┘
       │
       │ token = "j8fH3k9L..." (44 chars)
       │
       ▼
┌──────────────────────────┐
│ Create Event             │
│ {                        │
│   event_id: "...",       │
│   tokenized_pan: token,  │  ⚠️ PAN JAMAIS ici
│   ...                    │
│ }                        │
└──────────────────────────┘
       │
       │ (2) Clear PAN from memory
       │
       ▼
┌──────────────────────────┐
│ Send to Backend          │
│ (via gRPC/MQTT)          │
└──────────────────────────┘

Backend Side (Reception):
┌──────────────────────────┐
│ Receive Event            │
│ tokenized_pan: "j8fH..." │  ✅ Token seulement
└──────────────────────────┘
       │
       │ (3) Store token (Blob Storage)
       │     PAN original jamais connu par backend
       │
       ▼
┌──────────────────────────┐
│ Blacklist Comparison     │
│ if (token in blacklist)  │
│    → Alert               │
└──────────────────────────┘
```

### Key Management

**Secret Key** :
- Taille : 256 bits (32 bytes)
- Génération : CSPRNG (Cryptographically Secure PRNG)
- Stockage :
  - **Device** : Secure enclave / TEE si disponible
  - **Backend** : Azure Key Vault
- Rotation : Tous les 90 jours (PCI-DSS)

**Salt** :
- Taille : 128 bits (16 bytes)
- Unique par environnement (dev/staging/prod)
- Publiquement partageable (non secret)

### Rotation de clés

```bash
# Rotation tous les 90 jours
# 1. Générer nouvelle clé
NEW_KEY=$(openssl rand -base64 32)

# 2. Stocker dans Key Vault avec version
az keyvault secret set \
  --vault-name deltalist-kv \
  --name pan-secret-key \
  --value "$NEW_KEY" \
  --tags version=v2 rotation-date=$(date -I)

# 3. Rolling update devices (phase progressive)
# - Devices reçoivent nouvelle clé via config update
# - Période de transition : 7 jours (dual-key support)
# - Après transition : ancienne clé désactivée

# 4. Re-tokenization des blacklists existantes
# (si besoin de comparaison avec anciens tokens)
```

---

## Authentification et Autorisation

### JWT Authentication Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    JWT Authentication Flow                      │
└─────────────────────────────────────────────────────────────────┘

Device                     Auth Service              Backend
  │                             │                       │
  │ (1) Request token           │                       │
  │ POST /auth/register         │                       │
  │ Body: {                     │                       │
  │   device_id: "dev-001",     │                       │
  │   secret: "device_secret"   │                       │
  │ }                           │                       │
  ├────────────────────────────>│                       │
  │                             │                       │
  │                             │ (2) Validate device   │
  │                             │     credentials       │
  │                             │                       │
  │                             │ (3) Generate JWT      │
  │                             │     - Sign with RS256 │
  │                             │     - Expiration: 24h │
  │                             │     - Claims:         │
  │                             │       * sub: device_id│
  │                             │       * iat, exp      │
  │                             │       * roles: [...]  │
  │                             │                       │
  │ (4) Return JWT              │                       │
  │<────────────────────────────┤                       │
  │ {                           │                       │
  │   token: "eyJhbGc...",      │                       │
  │   expires_in: 86400         │                       │
  │ }                           │                       │
  │                             │                       │
  │ (5) Connect to backend      │                       │
  │     with JWT in metadata    │                       │
  ├─────────────────────────────────────────────────────>│
  │ Metadata:                   │                       │
  │   authorization: Bearer eyJ...                      │
  │                             │                       │
  │                             │          (6) Validate JWT
  │                             │              - Verify signature
  │                             │              - Check expiration
  │                             │              - Extract device_id
  │                             │                       │
  │                             │          (7) Authorize
  │                             │              - Check ACL
  │                             │              - Rate limit
  │                             │                       │
  │ (8) Connection accepted     │                       │
  │<─────────────────────────────────────────────────────┤
  │                             │                       │
```

### JWT Token Structure

```json
// Header
{
  "alg": "RS256",
  "typ": "JWT",
  "kid": "deltalist-signing-key-2025-01"
}

// Payload
{
  "sub": "device-000001",           // Subject: Device ID
  "iss": "DeltaList Auth Service",  // Issuer
  "aud": "DeltaList Backend",       // Audience
  "iat": 1642521600,                // Issued At
  "exp": 1642608000,                // Expiration (24h)
  "nbf": 1642521600,                // Not Before
  "jti": "unique-token-id",         // JWT ID (pour revocation)
  "device_id": "device-000001",     // Custom claim
  "roles": ["device", "transaction"], // Roles
  "rate_limit": 100                 // Custom: requests/min
}

// Signature
RSASHA256(
  base64UrlEncode(header) + "." + base64UrlEncode(payload),
  private_key
)
```

### Validation côté Backend

```csharp
public class JwtAuthenticationHandler
{
    private readonly IConfiguration _config;
    private readonly TokenValidationParameters _validationParams;

    public JwtAuthenticationHandler(IConfiguration config)
    {
        _validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "DeltaList Auth Service",

            ValidateAudience = true,
            ValidAudience = "DeltaList Backend",

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = LoadPublicKey(), // RSA public key

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5), // Tolérance

            RequireExpirationTime = true,
            RequireSignedTokens = true
        };
    }

    public async Task<ClaimsPrincipal> ValidateTokenAsync(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _validationParams, out var validatedToken);

            // Additional checks
            var deviceId = principal.FindFirst("device_id")?.Value;
            if (string.IsNullOrEmpty(deviceId))
            {
                throw new SecurityTokenException("device_id claim missing");
            }

            // Check revocation (Redis cache)
            var jti = principal.FindFirst("jti")?.Value;
            if (await _cache.ExistsAsync($"revoked_tokens:{jti}"))
            {
                throw new SecurityTokenException("Token has been revoked");
            }

            return principal;
        }
        catch (SecurityTokenExpiredException)
        {
            throw new UnauthorizedAccessException("Token expired");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            throw new UnauthorizedAccessException("Invalid token");
        }
    }
}
```

### Token Revocation

```csharp
public class TokenRevocationService
{
    private readonly IDistributedCache _cache;

    public async Task RevokeTokenAsync(string jti, DateTimeOffset expiration)
    {
        // Stocker JTI dans Redis avec TTL = temps restant jusqu'à expiration
        var ttl = expiration - DateTimeOffset.UtcNow;
        await _cache.SetStringAsync(
            $"revoked_tokens:{jti}",
            "revoked",
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = expiration
            });
    }
}
```

---

## Transport Layer Security (TLS)

### Configuration TLS

#### gRPC Backend

```csharp
// Program.cs - gRPC Backend
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;

        listenOptions.UseHttps(httpsOptions =>
        {
            // TLS 1.2 minimum
            httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

            // Cipher suites (secure only)
            // Note: .NET uses OS cipher suite configuration

            // Certificate
            var certPath = builder.Configuration["Certificates:Path"];
            var certPassword = builder.Configuration["Certificates:Password"];
            httpsOptions.ServerCertificate = new X509Certificate2(certPath, certPassword);

            // Client certificate validation (optional mTLS)
            httpsOptions.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            httpsOptions.ClientCertificateValidation = (cert, chain, errors) =>
            {
                // Custom validation logic
                return ValidateClientCertificate(cert, chain, errors);
            };
        });
    });
});
```

#### MQTT Backend (EMQX)

```yaml
# EMQX Configuration - emqx.conf
listeners.ssl.default {
  bind = "0.0.0.0:8883"

  # TLS versions
  tls_versions = "tlsv1.2,tlsv1.3"

  # Cipher suites (secure only)
  ciphers = "ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-AES128-GCM-SHA256"

  # Certificates
  keyfile = "/etc/emqx/certs/server.key"
  certfile = "/etc/emqx/certs/server.crt"
  cacertfile = "/etc/emqx/certs/ca.crt"

  # Client certificate verification (mTLS)
  verify = verify_peer
  fail_if_no_peer_cert = false  # true for strict mTLS

  # Depth of certificate chain
  depth = 10

  # Perfect Forward Secrecy
  honor_cipher_order = on
  reuse_sessions = on
}
```

### Certificate Management

**Production certificates** :
```bash
# Utiliser Azure Key Vault Certificate ou Let's Encrypt

# 1. Generate certificate request
openssl req -new -newkey rsa:2048 -nodes \
  -keyout deltalist.key \
  -out deltalist.csr \
  -subj "/CN=deltalist.example.com/O=DeltaList/C=US"

# 2. Submit CSR to CA (Azure Key Vault, DigiCert, etc.)

# 3. Store certificate in Key Vault
az keyvault certificate import \
  --vault-name deltalist-kv \
  --name deltalist-tls-cert \
  --file deltalist.pfx \
  --password <cert-password>

# 4. Mount certificate in Kubernetes pod
# Via CSI driver: secrets-store.csi.k8s.io
```

**Certificate rotation** :
```yaml
# Kubernetes CronJob for certificate renewal
apiVersion: batch/v1
kind: CronJob
metadata:
  name: cert-renewal
spec:
  schedule: "0 0 1 * *"  # Monthly
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: cert-renewer
            image: azure/cert-renewer:latest
            env:
            - name: KEY_VAULT_NAME
              value: deltalist-kv
            - name: CERT_NAME
              value: deltalist-tls-cert
          restartPolicy: OnFailure
```

### Cipher Suites (Recommandés)

```
# TLS 1.3 (preferred)
TLS_AES_256_GCM_SHA384
TLS_AES_128_GCM_SHA256
TLS_CHACHA20_POLY1305_SHA256

# TLS 1.2 (fallback)
ECDHE-RSA-AES256-GCM-SHA384
ECDHE-RSA-AES128-GCM-SHA256
ECDHE-ECDSA-AES256-GCM-SHA384
ECDHE-ECDSA-AES128-GCM-SHA256

# ⚠️ ÉVITER:
# - NULL ciphers
# - EXPORT ciphers
# - DES/3DES
# - RC4
# - MD5
```

---

## Secrets Management

### Azure Key Vault Integration

```
┌─────────────────────────────────────────────────────────────────┐
│                  Secrets Management Flow                        │
└─────────────────────────────────────────────────────────────────┘

Build Time                 Deploy Time              Runtime
    │                          │                       │
    │                          │                       │
    │ (1) No secrets in code   │                       │
    │     or config files      │                       │
    │                          │                       │
    │ (2) Secrets stored in    │                       │
    │     Azure Key Vault:     │                       │
    │     - pan-secret-key     │                       │
    │     - pan-salt           │                       │
    │     - jwt-signing-key    │                       │
    │     - redis-password     │                       │
    │                          │                       │
    │                          │ (3) AKS pod identity  │
    │                          │     (Managed Identity)│
    │                          │                       │
    │                          │ (4) CSI driver mounts │
    │                          │     secrets as files  │
    │                          │                       │
    │                          │                       │ (5) App reads
    │                          │                       │     secrets
    │                          │                       │     from files
    │                          │                       │
```

### Kubernetes Secrets Store CSI Driver

```yaml
# secretproviderclass.yaml
apiVersion: secrets-store.csi.x-k8s.io/v1
kind: SecretProviderClass
metadata:
  name: deltalist-secrets
  namespace: deltalist
spec:
  provider: azure
  parameters:
    usePodIdentity: "false"
    useVMManagedIdentity: "true"
    userAssignedIdentityID: "<managed-identity-client-id>"
    keyvaultName: "deltalist-kv"
    cloudName: "AzurePublicCloud"
    objects: |
      array:
        - |
          objectName: pan-secret-key
          objectType: secret
          objectVersion: ""
        - |
          objectName: pan-salt
          objectType: secret
          objectVersion: ""
        - |
          objectName: jwt-signing-key
          objectType: secret
          objectVersion: ""
        - |
          objectName: redis-password
          objectType: secret
          objectVersion: ""
    tenantId: "<azure-tenant-id>"
```

```yaml
# deployment.yaml - gRPC Backend
apiVersion: apps/v1
kind: Deployment
metadata:
  name: grpc-backend
spec:
  template:
    spec:
      containers:
      - name: grpc-backend
        image: deltalistacr.azurecr.io/grpc-backend:latest
        volumeMounts:
        - name: secrets-store
          mountPath: "/mnt/secrets"
          readOnly: true
        env:
        - name: SECURITY__SECRET_KEY_PATH
          value: "/mnt/secrets/pan-secret-key"
        - name: SECURITY__SALT_PATH
          value: "/mnt/secrets/pan-salt"
      volumes:
      - name: secrets-store
        csi:
          driver: secrets-store.csi.k8s.io
          readOnly: true
          volumeAttributes:
            secretProviderClass: "deltalist-secrets"
```

### Loading Secrets in Application

```csharp
// Startup.cs
public class SecurityConfiguration
{
    public static void ConfigureSecrets(IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IPANTokenizer>(sp =>
        {
            // Load secrets from mounted files (Key Vault via CSI)
            var secretKeyPath = config["Security:SecretKeyPath"];
            var saltPath = config["Security:SaltPath"];

            byte[] secretKey;
            byte[] salt;

            if (File.Exists(secretKeyPath))
            {
                // Production: Read from Key Vault mount
                var secretKeyB64 = File.ReadAllText(secretKeyPath).Trim();
                secretKey = Convert.FromBase64String(secretKeyB64);

                var saltB64 = File.ReadAllText(saltPath).Trim();
                salt = Convert.FromBase64String(saltB64);
            }
            else
            {
                // Development: Read from appsettings (⚠️ NON-production only!)
                var logger = sp.GetRequiredService<ILogger<SecurityConfiguration>>();
                logger.LogWarning("Using secrets from configuration (development mode)");

                secretKey = Convert.FromBase64String(config["Security:SecretKey"]);
                salt = Convert.FromBase64String(config["Security:Salt"]);
            }

            return new PANTokenizer(secretKey, salt);
        });
    }
}
```

---

## ACL et Isolation

### gRPC - Metadata-based Authorization

```csharp
public class DeviceAuthorizationInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        // Extract device_id from JWT claims
        var claimedDeviceId = context.GetHttpContext()
            .User.FindFirst("device_id")?.Value;

        if (string.IsNullOrEmpty(claimedDeviceId))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated,
                "Missing device_id claim"));
        }

        // If request contains device_id field, validate it matches JWT claim
        if (request is IHasDeviceId deviceRequest)
        {
            if (deviceRequest.DeviceId != claimedDeviceId)
            {
                // Device trying to impersonate another device
                _logger.LogWarning(
                    "Authorization violation: Device {ClaimedId} tried to access {RequestedId}",
                    claimedDeviceId, deviceRequest.DeviceId);

                throw new RpcException(new Status(StatusCode.PermissionDenied,
                    "Device ID mismatch"));
            }
        }

        // Store in context for downstream use
        context.UserState["authorized_device_id"] = claimedDeviceId;

        return await continuation(request, context);
    }
}
```

### MQTT - EMQX ACL

```erlang
%% EMQX ACL Configuration
%% File: /etc/emqx/acl.conf

%% 1. Deny all by default
{deny, all}.

%% 2. Allow devices to publish ONLY to their own topics
{allow, {user, "device-000001"}, publish, ["devices/device-000001/events"]}.
{allow, {user, "device-000001"}, publish, ["devices/device-000001/status"]}.

%% 3. Allow devices to subscribe to their own commands + global blacklist
{allow, {user, "device-000001"}, subscribe, ["devices/device-000001/commands"]}.
{allow, {user, "device-000001"}, subscribe, ["blacklist/#"]}.

%% 4. Backend service can publish/subscribe to all
{allow, {user, "backend-service"}, publish, ["#"]}.
{allow, {user, "backend-service"}, subscribe, ["#"]}.

%% 5. Specific role for monitoring
{allow, {user, "monitoring"}, subscribe, ["$SYS/#"]}.

%% Pattern matching example:
%% Device username format: device-{id}
%% Allow publish to devices/{id}/#
{allow, {username, "^device-(.+)$"}, publish, ["devices/%1/#"]}.
{allow, {username, "^device-(.+)$"}, subscribe, ["devices/%1/commands", "blacklist/#"]}.
```

### Network Isolation (AKS)

```yaml
# NetworkPolicy - Isolate PoC A and PoC B
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: grpc-backend-policy
  namespace: deltalist
spec:
  podSelector:
    matchLabels:
      app: grpc-backend
  policyTypes:
  - Ingress
  - Egress
  ingress:
  # Allow from load balancer only
  - from:
    - namespaceSelector:
        matchLabels:
          name: ingress-nginx
    ports:
    - protocol: TCP
      port: 5001
  - from:
    - podSelector:
        matchLabels:
          app: prometheus
    ports:
    - protocol: TCP
      port: 9090
  egress:
  # Allow to Redis
  - to:
    - podSelector:
        matchLabels:
          app: redis
    ports:
    - protocol: TCP
      port: 6379
  # Allow to Azure Blob (via HTTPS)
  - to:
    - namespaceSelector: {}
    ports:
    - protocol: TCP
      port: 443
  # Allow to Key Vault
  - to:
    - namespaceSelector: {}
    ports:
    - protocol: TCP
      port: 443
  # Block everything else
```

---

## Audit Logging

### Structured Logging

```csharp
public class AuditLogger : IAuditLogger
{
    private readonly ILogger<AuditLogger> _logger;

    public void LogAuthentication(string deviceId, bool success, string reason = null)
    {
        var logLevel = success ? LogLevel.Information : LogLevel.Warning;

        _logger.Log(logLevel, new EventId(1001, "Authentication"),
            "Device authentication: {DeviceId}, Success: {Success}, Reason: {Reason}",
            deviceId, success, reason ?? "N/A");

        // Enriched log entry
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["EventType"] = "Authentication",
            ["DeviceId"] = deviceId,
            ["Success"] = success,
            ["Timestamp"] = DateTimeOffset.UtcNow,
            ["CorrelationId"] = Activity.Current?.Id
        }))
        {
            _logger.LogInformation("Authentication event logged");
        }
    }

    public void LogBlacklistAccess(string deviceId, string action, int entryCount)
    {
        _logger.Log(LogLevel.Information, new EventId(2001, "BlacklistAccess"),
            "Blacklist access: {DeviceId}, Action: {Action}, Entries: {Count}",
            deviceId, action, entryCount);
    }

    public void LogDataAccess(string deviceId, string operation, string resourceId)
    {
        _logger.Log(LogLevel.Information, new EventId(3001, "DataAccess"),
            "Data access: {DeviceId}, Operation: {Operation}, Resource: {ResourceId}",
            deviceId, operation, resourceId);
    }

    public void LogSecurityViolation(string deviceId, string violationType, string details)
    {
        _logger.LogWarning(new EventId(9001, "SecurityViolation"),
            "Security violation: {DeviceId}, Type: {ViolationType}, Details: {Details}",
            deviceId, violationType, details);

        // Send alert (e.g., Azure Monitor alert)
        SendSecurityAlert(deviceId, violationType, details);
    }
}
```

### Log Retention

```yaml
# Azure Log Analytics Workspace configuration
# Retention: 90 days (PCI-DSS minimum)

apiVersion: v1
kind: ConfigMap
metadata:
  name: fluentd-config
  namespace: kube-system
data:
  fluent.conf: |
    <match **>
      @type azure-loganalytics
      workspace_id "#{ENV['WORKSPACE_ID']}"
      shared_key "#{ENV['SHARED_KEY']}"

      # Log types
      log_type DeltaListAuditLogs

      # Buffering
      <buffer>
        @type file
        path /var/log/fluentd-buffer
        flush_interval 10s
        chunk_limit_size 5M
      </buffer>

      # Include metadata
      include_tag_key true
      tag_key _tag

      # Add common fields
      <inject>
        time_key timestamp
        time_type string
        time_format %Y-%m-%dT%H:%M:%S.%NZ
      </inject>
    </match>
```

---

## Security Checklist

### Pre-Deployment Security Checklist

```markdown
## Infrastructure
- [ ] TLS 1.2+ configured for all endpoints
- [ ] Cipher suites validated (no weak ciphers)
- [ ] Certificates from trusted CA
- [ ] Certificate expiration monitoring enabled
- [ ] Network policies applied (namespace isolation)
- [ ] Azure DDoS Protection enabled
- [ ] Azure Firewall configured

## Authentication & Authorization
- [ ] JWT authentication enabled
- [ ] Token expiration set (≤ 24h)
- [ ] Token revocation mechanism implemented
- [ ] ACL configured (gRPC metadata / MQTT ACL)
- [ ] Device ID validation enforced
- [ ] Rate limiting enabled

## Data Protection
- [ ] PAN tokenization verified (no plaintext PAN)
- [ ] Secrets stored in Azure Key Vault (not in code/config)
- [ ] CSI driver configured for secret injection
- [ ] Encryption at rest enabled (Azure Blob, Redis)
- [ ] Encryption in transit enforced (TLS)

## Logging & Monitoring
- [ ] Audit logging configured
- [ ] Log retention set (90+ days)
- [ ] Security alerts configured
- [ ] Prometheus metrics exposed
- [ ] Grafana dashboards created
- [ ] Alert rules defined (failed auth, rate limit, etc.)

## Compliance
- [ ] PCI-DSS requirements validated
- [ ] Security documentation complete
- [ ] Incident response plan documented
- [ ] Vulnerability scanning scheduled
- [ ] Penetration testing planned

## Application Security
- [ ] Input validation implemented
- [ ] Protobuf message size limits configured
- [ ] Resource limits set (CPU, memory, connections)
- [ ] Graceful degradation implemented
- [ ] Error messages sanitized (no sensitive data leakage)
```

### Runtime Security Monitoring

```yaml
# Prometheus AlertManager rules
groups:
- name: security_alerts
  interval: 30s
  rules:
  # Failed authentication spike
  - alert: HighAuthenticationFailureRate
    expr: rate(authentication_failures_total[5m]) > 10
    for: 5m
    labels:
      severity: warning
    annotations:
      summary: "High authentication failure rate detected"
      description: "{{ $value }} failures per second in the last 5 minutes"

  # Unauthorized access attempts
  - alert: UnauthorizedAccessAttempts
    expr: increase(authorization_denied_total[10m]) > 50
    labels:
      severity: critical
    annotations:
      summary: "Multiple unauthorized access attempts"

  # Token revocations
  - alert: TokenRevocationSpike
    expr: rate(token_revocations_total[15m]) > 5
    labels:
      severity: warning
    annotations:
      summary: "Unusual number of token revocations"

  # TLS errors
  - alert: TLSHandshakeFailures
    expr: rate(tls_handshake_errors_total[5m]) > 1
    labels:
      severity: warning
    annotations:
      summary: "TLS handshake failures detected"
```

---

## Vulnerability Reporting

### Responsible Disclosure Policy

Si vous découvrez une vulnérabilité de sécurité dans DeltaList, merci de nous la signaler de manière responsable.

**Ne PAS** :
- Divulguer publiquement la vulnérabilité avant correction
- Exploiter la vulnérabilité au-delà du strict nécessaire pour la démonstration
- Accéder aux données de production

**Procédure** :

1. **Rapport initial**
   - Email : security@deltalist.example.com
   - PGP Key : [Disponible sur keybase.io]
   - Inclure :
     - Description détaillée de la vulnérabilité
     - Steps to reproduce
     - Impact estimé (CVSS score si possible)
     - Votre nom/pseudo pour crédit (optionnel)

2. **Acknowledgement**
   - Nous accusons réception sous 48h
   - Attribution d'un tracking ID (DLSA-YYYY-NNNN)

3. **Investigation**
   - Analyse de la vulnérabilité : 1-7 jours
   - Développement du patch : selon gravité
   - Communication régulière sur l'avancement

4. **Résolution**
   - Déploiement du patch
   - Publication d'un security advisory
   - Crédit au chercheur (si souhaité)

5. **Disclosure Timeline**
   - Critical (CVSS 9-10) : Patch sous 7 jours
   - High (CVSS 7-8.9) : Patch sous 30 jours
   - Medium/Low : Patch selon roadmap

### Bug Bounty (Future)

Un programme de bug bounty pourrait être lancé à l'avenir. Restez à l'écoute !

---

## Références

- [PCI DSS v4.0](https://www.pcisecuritystandards.org/)
- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [Azure Security Best Practices](https://learn.microsoft.com/en-us/azure/security/fundamentals/best-practices-and-patterns)
- [NIST Cryptographic Standards](https://csrc.nist.gov/)
- [gRPC Security Guide](https://grpc.io/docs/guides/auth/)
- [EMQX Security](https://www.emqx.io/docs/en/latest/security/overview.html)

---

**Document version** : 1.0.0
**Last updated** : 2025-01-17
**Security contact** : security@deltalist.example.com
