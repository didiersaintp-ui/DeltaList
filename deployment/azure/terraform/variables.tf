variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
  default     = "dev"
}

variable "prefix" {
  description = "Prefix for resource names"
  type        = string
  default     = "deltalist"
}

variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
  default     = "deltalist-rg"
}

variable "location" {
  description = "Azure region"
  type        = string
  default     = "westeurope"
}

variable "kubernetes_version" {
  description = "Kubernetes version"
  type        = string
  default     = "1.28"
}

variable "node_count" {
  description = "Initial number of nodes"
  type        = number
  default     = 3
}

variable "min_node_count" {
  description = "Minimum number of nodes for autoscaling"
  type        = number
  default     = 3
}

variable "max_node_count" {
  description = "Maximum number of nodes for autoscaling"
  type        = number
  default     = 10
}

variable "node_vm_size" {
  description = "VM size for AKS nodes"
  type        = string
  default     = "Standard_D4s_v3"
  # For production load testing, consider:
  # - Standard_D4s_v3: 4 vCPU, 16 GB RAM
  # - Standard_D8s_v3: 8 vCPU, 32 GB RAM
  # - Standard_D16s_v3: 16 vCPU, 64 GB RAM
}

variable "common_tags" {
  description = "Common tags for all resources"
  type        = map(string)
  default = {
    Project     = "DeltaList"
    ManagedBy   = "Terraform"
    CostCenter  = "R&D"
  }
}
