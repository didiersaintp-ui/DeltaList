terraform {
  required_version = ">= 1.5.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.80"
    }
  }

  backend "azurerm" {
    # Configure backend storage for state
    # Can be overridden with backend-config during init
  }
}

provider "azurerm" {
  features {
    resource_group {
      prevent_deletion_if_contains_resources = false
    }
  }
}

# Resource Group
resource "azurerm_resource_group" "deltalist" {
  name     = var.resource_group_name
  location = var.location

  tags = merge(var.common_tags, {
    Environment = var.environment
    Project     = "DeltaList"
  })
}

# Virtual Network
resource "azurerm_virtual_network" "deltalist" {
  name                = "${var.prefix}-vnet"
  location            = azurerm_resource_group.deltalist.location
  resource_group_name = azurerm_resource_group.deltalist.name
  address_space       = ["10.0.0.0/16"]

  tags = var.common_tags
}

# AKS Subnet
resource "azurerm_subnet" "aks" {
  name                 = "${var.prefix}-aks-subnet"
  resource_group_name  = azurerm_resource_group.deltalist.name
  virtual_network_name = azurerm_virtual_network.deltalist.name
  address_prefixes     = ["10.0.1.0/24"]
}

# Azure Container Registry
resource "azurerm_container_registry" "deltalist" {
  name                = "${var.prefix}acr"
  resource_group_name = azurerm_resource_group.deltalist.name
  location            = azurerm_resource_group.deltalist.location
  sku                 = "Standard"
  admin_enabled       = true

  tags = var.common_tags
}

# Log Analytics Workspace
resource "azurerm_log_analytics_workspace" "deltalist" {
  name                = "${var.prefix}-logs"
  location            = azurerm_resource_group.deltalist.location
  resource_group_name = azurerm_resource_group.deltalist.name
  sku                 = "PerGB2018"
  retention_in_days   = 30

  tags = var.common_tags
}

# AKS Cluster
resource "azurerm_kubernetes_cluster" "deltalist" {
  name                = "${var.prefix}-aks"
  location            = azurerm_resource_group.deltalist.location
  resource_group_name = azurerm_resource_group.deltalist.name
  dns_prefix          = "${var.prefix}-aks"
  kubernetes_version  = var.kubernetes_version

  default_node_pool {
    name                = "default"
    node_count          = var.node_count
    vm_size             = var.node_vm_size
    vnet_subnet_id      = azurerm_subnet.aks.id
    enable_auto_scaling = true
    min_count           = var.min_node_count
    max_count           = var.max_node_count
    os_disk_size_gb     = 100

    tags = var.common_tags
  }

  identity {
    type = "SystemAssigned"
  }

  network_profile {
    network_plugin    = "azure"
    network_policy    = "azure"
    load_balancer_sku = "standard"
    service_cidr      = "10.1.0.0/16"
    dns_service_ip    = "10.1.0.10"
  }

  oms_agent {
    log_analytics_workspace_id = azurerm_log_analytics_workspace.deltalist.id
  }

  azure_policy_enabled = true

  tags = var.common_tags
}

# Role assignment for ACR pull
resource "azurerm_role_assignment" "aks_acr_pull" {
  principal_id                     = azurerm_kubernetes_cluster.deltalist.kubelet_identity[0].object_id
  role_definition_name             = "AcrPull"
  scope                            = azurerm_container_registry.deltalist.id
  skip_service_principal_aad_check = true
}

# Application Insights
resource "azurerm_application_insights" "deltalist" {
  name                = "${var.prefix}-appinsights"
  location            = azurerm_resource_group.deltalist.location
  resource_group_name = azurerm_resource_group.deltalist.name
  workspace_id        = azurerm_log_analytics_workspace.deltalist.id
  application_type    = "web"

  tags = var.common_tags
}
