output "resource_group_name" {
  description = "Name of the resource group"
  value       = azurerm_resource_group.deltalist.name
}

output "aks_cluster_name" {
  description = "Name of the AKS cluster"
  value       = azurerm_kubernetes_cluster.deltalist.name
}

output "aks_cluster_id" {
  description = "ID of the AKS cluster"
  value       = azurerm_kubernetes_cluster.deltalist.id
}

output "aks_kubeconfig_command" {
  description = "Command to get kubeconfig"
  value       = "az aks get-credentials --resource-group ${azurerm_resource_group.deltalist.name} --name ${azurerm_kubernetes_cluster.deltalist.name}"
}

output "acr_name" {
  description = "Name of the Azure Container Registry"
  value       = azurerm_container_registry.deltalist.name
}

output "acr_login_server" {
  description = "Login server for ACR"
  value       = azurerm_container_registry.deltalist.login_server
}

output "log_analytics_workspace_id" {
  description = "ID of the Log Analytics Workspace"
  value       = azurerm_log_analytics_workspace.deltalist.id
}

output "application_insights_instrumentation_key" {
  description = "Application Insights instrumentation key"
  value       = azurerm_application_insights.deltalist.instrumentation_key
  sensitive   = true
}

output "application_insights_connection_string" {
  description = "Application Insights connection string"
  value       = azurerm_application_insights.deltalist.connection_string
  sensitive   = true
}
