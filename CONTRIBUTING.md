# Contributing to DeltaList

Merci de votre intérêt pour contribuer à DeltaList ! Ce guide vous aidera à contribuer efficacement au projet.

## Table des Matières

- [Code of Conduct](#code-of-conduct)
- [Comment contribuer](#comment-contribuer)
- [Processus de développement](#processus-de-développement)
- [Standards de code](#standards-de-code)
- [Convention de commit](#convention-de-commit)
- [Processus de Pull Request](#processus-de-pull-request)
- [Standards de test](#standards-de-test)
- [Documentation](#documentation)

---

## Code of Conduct

Ce projet suit le [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). En participant, vous acceptez de respecter ce code.

**Principes clés** :
- Respect et bienveillance envers tous les contributeurs
- Critique constructive du code, jamais des personnes
- Ouverture aux différentes perspectives et expériences
- Focus sur ce qui est meilleur pour la communauté

---

## Comment contribuer

### Types de contributions

Nous acceptons plusieurs types de contributions :

**1. Rapports de bugs**
- Utiliser les GitHub Issues
- Template : [Bug Report](.github/ISSUE_TEMPLATE/bug_report.md)
- Inclure : Steps to reproduce, environnement, logs

**2. Demandes de fonctionnalités**
- Utiliser les GitHub Issues
- Template : [Feature Request](.github/ISSUE_TEMPLATE/feature_request.md)
- Décrire : Use case, solution proposée, alternatives

**3. Corrections de bugs**
- Créer une issue d'abord (sauf typos évidentes)
- Lier PR à l'issue
- Ajouter tests

**4. Nouvelles fonctionnalités**
- Discuter dans une issue d'abord
- Accord de mainteneur requis avant implémentation
- Documentation obligatoire

**5. Améliorations de documentation**
- Toujours bienvenues !
- Pas besoin d'issue pour typos/clarifications mineures

---

## Processus de développement

### 1. Fork et Clone

```bash
# Fork le repository sur GitHub
# Puis cloner votre fork
git clone https://github.com/YOUR-USERNAME/DeltaList.git
cd DeltaList

# Ajouter upstream remote
git remote add upstream https://github.com/ORIGINAL-OWNER/DeltaList.git
```

### 2. Créer une branche

```bash
# Synchroniser avec upstream
git fetch upstream
git checkout main
git merge upstream/main

# Créer branche feature
git checkout -b feature/ma-nouvelle-feature

# Ou pour bugfix
git checkout -b fix/correction-bug-123
```

**Convention de nommage des branches** :
- `feature/description-courte` : Nouvelle fonctionnalité
- `fix/description-bug` : Correction de bug
- `docs/description` : Mise à jour documentation
- `refactor/description` : Refactoring code
- `test/description` : Ajout/amélioration tests
- `chore/description` : Tâches maintenance (deps, config, etc.)

### 3. Développer

```bash
# Compiler
dotnet restore
dotnet build

# Lancer tests
dotnet test

# Lancer localement (Docker Compose)
docker-compose -f docker-compose.grpc.yml up

# Vérifier que tout fonctionne
./scripts/validate-deployment.sh grpc
```

### 4. Commiter

Voir [Convention de commit](#convention-de-commit) ci-dessous.

### 5. Pousser et créer PR

```bash
# Pousser vers votre fork
git push origin feature/ma-nouvelle-feature

# Créer Pull Request sur GitHub
# Utiliser le template: .github/PULL_REQUEST_TEMPLATE.md
```

---

## Standards de code

### Style C# (.NET 8)

Nous suivons les [conventions Microsoft C#](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) avec quelques ajouts :

**Général** :
```csharp
// ✅ Bon
public class DeviceConnectionManager
{
    private readonly ILogger<DeviceConnectionManager> _logger;
    private readonly ConcurrentDictionary<string, DeviceConnection> _connections;

    public async Task<bool> RegisterAsync(string deviceId, DeviceConnection connection)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            throw new ArgumentException("Device ID cannot be null or empty", nameof(deviceId));
        }

        return _connections.TryAdd(deviceId, connection);
    }
}

// ❌ Mauvais
public class deviceconnectionmanager  // PascalCase requis
{
    private ILogger logger;  // Pas de _ prefix pour fields privés

    public bool Register(string id, DeviceConnection conn)  // Synchrone au lieu d'async
    {
        _connections.TryAdd(id, conn);  // Pas de validation
    }
}
```

**Naming conventions** :
- `PascalCase` : Classes, méthodes, propriétés publiques
- `camelCase` : Paramètres, variables locales
- `_camelCase` : Champs privés (avec underscore prefix)
- `SCREAMING_CASE` : Constantes

**Async/await** :
```csharp
// ✅ Toujours async jusqu'au bout
public async Task ProcessBatchAsync(Batch batch)
{
    await _validator.ValidateAsync(batch);
    await _storage.StoreBatchAsync(batch);
}

// ❌ Éviter .Result et .Wait()
public void ProcessBatch(Batch batch)
{
    _validator.ValidateAsync(batch).Wait();  // ❌ Deadlock risk
}
```

**Gestion d'erreurs** :
```csharp
// ✅ Exceptions spécifiques
public async Task<Batch> GetBatchAsync(string id)
{
    if (string.IsNullOrEmpty(id))
    {
        throw new ArgumentException("ID cannot be null", nameof(id));
    }

    var batch = await _repository.FindAsync(id);
    if (batch == null)
    {
        throw new NotFoundException($"Batch {id} not found");
    }

    return batch;
}

// ❌ Exception générique
public async Task<Batch> GetBatchAsync(string id)
{
    try
    {
        return await _repository.FindAsync(id);
    }
    catch (Exception ex)
    {
        throw new Exception("Error");  // ❌ Pas assez spécifique
    }
}
```

### EditorConfig

Le projet inclut `.editorconfig` pour l'uniformité :

```ini
# .editorconfig
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true

[*.cs]
indent_style = space
indent_size = 4

[*.{yaml,yml}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false
```

### Linting

```bash
# Utiliser dotnet format
dotnet format

# Ou via IDE (Rider, VS, VSCode + C# extension)
```

---

## Convention de commit

Nous utilisons [Conventional Commits](https://www.conventionalcommits.org/).

### Format

```
<type>(<scope>): <subject>

[optional body]

[optional footer]
```

### Types

| Type | Description | Exemple |
|------|-------------|---------|
| `feat` | Nouvelle fonctionnalité | `feat(grpc): add JWT authentication` |
| `fix` | Correction de bug | `fix(mqtt): resolve reconnection issue` |
| `docs` | Documentation uniquement | `docs: update README quickstart` |
| `style` | Formatage (pas de changement de code) | `style: fix indentation in DeviceService` |
| `refactor` | Refactoring (pas de feat/fix) | `refactor(storage): extract blob writer` |
| `perf` | Amélioration performance | `perf(tokenization): optimize HMAC caching` |
| `test` | Ajout/modification tests | `test(simulator): add load test scenarios` |
| `chore` | Maintenance (deps, config) | `chore: update dependencies` |
| `ci` | CI/CD | `ci: add GitHub Actions workflow` |

### Scopes (optionnels)

- `grpc` : Backend gRPC
- `mqtt` : Backend MQTT ou EMQX
- `simulator` : Simulateurs devices
- `storage` : Blob/Redis storage
- `auth` : Authentification
- `security` : Sécurité
- `infra` : Infrastructure (Terraform, K8s)
- `docs` : Documentation

### Exemples

```bash
# Bons commits
git commit -m "feat(grpc): implement rate limiting middleware"
git commit -m "fix(mqtt): handle QoS 2 message duplication"
git commit -m "docs: add security documentation for PCI-DSS compliance"
git commit -m "perf(storage): batch blob writes for improved latency"
git commit -m "test(load): add 30k devices endurance test scenario"

# Mauvais commits
git commit -m "update"  # ❌ Pas assez descriptif
git commit -m "Fixed bug"  # ❌ Pas de type, pas de contexte
git commit -m "WIP"  # ❌ Ne pas commiter WIP sur main/develop
```

### Breaking Changes

Pour changements incompatibles :

```bash
git commit -m "feat(grpc)!: change Batch message structure

BREAKING CHANGE: Batch.events is now repeated field instead of map.
Clients must update their Protobuf definitions.

Migration guide: docs/MIGRATION_V2.md"
```

---

## Processus de Pull Request

### Avant de soumettre

**Checklist** :
- [ ] Code compile sans erreurs ni warnings
- [ ] Tests passent (`dotnet test`)
- [ ] Nouveaux tests ajoutés (si applicable)
- [ ] Documentation mise à jour (README, docs/, XML comments)
- [ ] Commits suivent convention
- [ ] Branch à jour avec `main` (rebase si nécessaire)

### Template PR

Utiliser le template `.github/PULL_REQUEST_TEMPLATE.md` :

```markdown
## Description
Brève description des changements

## Type de changement
- [ ] Bug fix
- [ ] Nouvelle fonctionnalité
- [ ] Breaking change
- [ ] Documentation

## Tests
Comment a été testé ?
- [ ] Tests unitaires
- [ ] Tests d'intégration
- [ ] Tests manuels

## Checklist
- [ ] Code compile
- [ ] Tests passent
- [ ] Documentation à jour
- [ ] Commits conventionnels
```

### Processus de review

1. **Automated checks** : CI/CD GitHub Actions
   - Build
   - Tests
   - Linting
   - Security scan

2. **Code review** : Au moins 1 approbation requise
   - Reviewer vérifie :
     - Qualité code
     - Tests adéquats
     - Documentation
     - Respect des standards

3. **Modifications** : Si changements demandés
   - Corriger et pousser sur même branche
   - PR se met à jour automatiquement
   - Re-review si nécessaire

4. **Merge** : Après approbation(s)
   - Squash & merge (défaut)
   - Message commit suit convention

### Après le merge

- Branche feature supprimée automatiquement
- Issue liée fermée automatiquement (si `Fixes #123` dans PR)
- Contributeur ajouté à `.github/CONTRIBUTORS.md`

---

## Standards de test

### Couverture requise

- **Nouveau code** : Minimum 70% coverage
- **Code critique (auth, tokenization)** : 90%+ coverage

```bash
# Générer rapport de couverture
dotnet test --collect:"XPlat Code Coverage"

# Rapport HTML
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

### Types de tests

**1. Tests unitaires**
```csharp
// xUnit
public class PANTokenizerTests
{
    [Fact]
    public void TokenizePAN_ValidPAN_ReturnsDeterministicToken()
    {
        // Arrange
        var tokenizer = new PANTokenizer(secretKey, salt);
        var pan = "1234567890123456";

        // Act
        var token1 = tokenizer.TokenizePAN(pan);
        var token2 = tokenizer.TokenizePAN(pan);

        // Assert
        Assert.Equal(token1, token2);  // Déterministe
        Assert.NotEqual(pan, token1);   // Pas en clair
        Assert.Equal(44, token1.Length); // Base64 de 256 bits
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]  // Trop court
    [InlineData("abcd1234567890123456")]  // Non-numérique
    public void TokenizePAN_InvalidPAN_ThrowsException(string invalidPAN)
    {
        var tokenizer = new PANTokenizer(secretKey, salt);
        Assert.Throws<SecurityException>(() => tokenizer.TokenizePAN(invalidPAN));
    }
}
```

**2. Tests d'intégration**
```bash
# Scripts Bash dans tests/integration-tests/
./tests/integration-tests/test_grpc_flow.sh
./tests/integration-tests/test_mqtt_flow.sh
```

**3. Tests de charge**
```bash
# Scripts Python dans tests/load-tests/
python3 tests/load-tests/load_test_runner.py --poc-type grpc --total-devices 1000
```

### Exécution des tests

```bash
# Tous les tests
dotnet test

# Tests d'un projet spécifique
dotnet test src/GrpcBackend.Tests/GrpcBackend.Tests.csproj

# Avec verbosité
dotnet test --logger "console;verbosity=detailed"

# Tests parallèles (plus rapide)
dotnet test --parallel
```

---

## Documentation

### Documentation requise

**1. XML Comments (C#)**
```csharp
/// <summary>
/// Tokenizes a PAN (Primary Account Number) using HMAC-SHA256.
/// </summary>
/// <param name="plainPAN">The PAN in plain text (13-19 digits).</param>
/// <returns>A Base64-encoded token (44 characters).</returns>
/// <exception cref="ArgumentException">Thrown if PAN format is invalid.</exception>
/// <exception cref="SecurityException">Thrown if tokenization fails.</exception>
/// <remarks>
/// This method uses one-way hashing. The original PAN cannot be recovered from the token.
/// Complies with PCI-DSS requirement 3.4.
/// </remarks>
public string TokenizePAN(string plainPAN)
{
    // ...
}
```

**2. README.md**
- Pour chaque nouveau composant majeur
- Placer dans le dossier du composant
- Inclure : Description, usage, configuration

**3. docs/**
- Architecture changes → `docs/ARCHITECTURE.md`
- Security impacts → `docs/SECURITY.md`
- Performance impacts → `docs/PERFORMANCE.md`
- FAQ → `docs/FAQ.md`

### Génération de documentation

```bash
# Swagger/OpenAPI (pour REST APIs)
# Généré automatiquement sur /swagger

# Protobuf documentation
# Généré automatiquement à partir de .proto avec commentaires
```

---

## Questions ?

- **Discussions** : GitHub Discussions
- **Chat** : Slack #deltalist-dev (si disponible)
- **Email** : dev@deltalist.example.com

---

**Merci de contribuer à DeltaList !** 🚀
