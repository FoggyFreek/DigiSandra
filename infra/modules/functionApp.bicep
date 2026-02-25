@description('Resource name suffix')
param name string

@description('Azure region')
param location string

@description('Application Insights instrumentation key')
param appInsightsInstrumentationKey string

@description('Application Insights connection string')
param appInsightsConnectionString string

@description('Cosmos DB endpoint')
param cosmosDbEndpoint string

@description('Cosmos DB database name')
param cosmosDbDatabaseName string

@description('Cosmos DB container name')
param cosmosDbContainerName string

@description('Azure OpenAI endpoint')
param openAiEndpoint string

@description('Azure OpenAI deployment name')
param openAiDeploymentName string

@description('Bot Microsoft App ID')
@secure()
param botAppId string

@description('Bot Microsoft App Password')
@secure()
param botAppPassword string

@description('Microsoft Graph Client ID')
@secure()
param graphClientId string

@description('Microsoft Graph Client Secret')
@secure()
param graphClientSecret string

@description('Microsoft Graph Tenant ID')
param graphTenantId string

var storageAccountName = replace(toLower('st${name}'), '-', '')
var functionAppName = 'func-${name}'
var hostingPlanName = 'plan-${name}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take(storageAccountName, 24)
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

resource hostingPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: hostingPlanName
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${az.environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}' }
        { name: 'WEBSITE_CONTENTSHARE', value: toLower(functionAppName) }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'APPINSIGHTS_INSTRUMENTATIONKEY', value: appInsightsInstrumentationKey }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'CosmosDb__Endpoint', value: cosmosDbEndpoint }
        { name: 'CosmosDb__DatabaseName', value: cosmosDbDatabaseName }
        { name: 'CosmosDb__ContainerName', value: cosmosDbContainerName }
        { name: 'CosmosDb__DefaultTtlSeconds', value: '604800' }
        { name: 'AzureOpenAI__Endpoint', value: openAiEndpoint }
        { name: 'AzureOpenAI__DeploymentName', value: openAiDeploymentName }
        { name: 'MicrosoftGraph__TenantId', value: graphTenantId }
        { name: 'MicrosoftGraph__ClientId', value: graphClientId }
        { name: 'MicrosoftGraph__ClientSecret', value: graphClientSecret }
        { name: 'Bot__MicrosoftAppId', value: botAppId }
        { name: 'Bot__MicrosoftAppPassword', value: botAppPassword }
        { name: 'ConflictResolution__TimeoutHours', value: '4' }
        { name: 'ConflictResolution__MaxRetries', value: '3' }
      ]
    }
  }
}

output name string = functionApp.name
output endpoint string = 'https://${functionApp.properties.defaultHostName}/api/messages'
output principalId string = functionApp.identity.principalId
