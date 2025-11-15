# Terraform - Déploiement Infrastructure Azure

Ce dossier contient les scripts Terraform pour déployer l'infrastructure Azure nécessaire aux PoC.

## Ressources créées

- **Resource Group** : Groupe de ressources Azure
- **Virtual Network** : Réseau virtuel avec subnets
- **AKS Cluster** : Cluster Kubernetes managé
- **Azure Container Registry** : Registry privé pour les images Docker
- **Log Analytics Workspace** : Collecte de logs et métriques
- **Application Insights** : Monitoring applicatif

## Prérequis

1. Azure CLI installé et configuré
2. Terraform >= 1.5.0
3. Credentials Azure configurés

```bash
az login
az account set --subscription <subscription-id>
```

## Utilisation

### 1. Initialisation

```bash
cd deployment/azure/terraform

# Copier le fichier d'exemple
cp terraform.tfvars.example terraform.tfvars

# Éditer terraform.tfvars avec vos valeurs
nano terraform.tfvars

# Initialiser Terraform
terraform init
```

### 2. Plan

```bash
terraform plan -out=tfplan
```

### 3. Apply

```bash
terraform apply tfplan
```

### 4. Récupérer les outputs

```bash
# Voir tous les outputs
terraform output

# Récupérer le kubeconfig AKS
terraform output -raw aks_kubeconfig_command | bash

# Vérifier la connexion
kubectl get nodes
```

### 5. Configuration du Container Registry

```bash
# Login à ACR
az acr login --name $(terraform output -raw acr_name)

# Créer le secret Kubernetes pour pull images
kubectl create secret docker-registry acr-secret \
  --namespace deltalist \
  --docker-server=$(terraform output -raw acr_login_server) \
  --docker-username=$(az acr credential show --name $(terraform output -raw acr_name) --query username -o tsv) \
  --docker-password=$(az acr credential show --name $(terraform output -raw acr_name) --query passwords[0].value -o tsv)
```

## Sizing pour les tests

### Test 1k devices
- Nodes: 3x Standard_D4s_v3 (4 vCPU, 16 GB)
- Coût estimé: ~300€/mois

### Test 5k devices
- Nodes: 5-7x Standard_D4s_v3
- Coût estimé: ~500-700€/mois

### Test 30k devices
- Nodes: 10-15x Standard_D8s_v3 (8 vCPU, 32 GB)
- Coût estimé: ~2000-3000€/mois

## Nettoyage

```bash
# Supprimer toutes les ressources
terraform destroy
```

## Sécurité

⚠️ **Important:**
- Ne jamais commiter `terraform.tfvars` avec des secrets
- Utiliser Azure Key Vault pour les secrets en production
- Activer Network Policies sur AKS
- Configurer des NSG restrictifs
