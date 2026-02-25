targetScope = 'resourceGroup'

@description('Environment name (dev, acc, prod)')
@allowed(['dev', 'acc', 'prod'])
param environment string

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Base name for all resources')
param baseName string = 'digisandra'

@description('Azure OpenAI deployment name')
param openAiDeploymentName string = 'gpt-4o'

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

var resourceSuffix = '${baseName}-${environment}'

// Monitoring
module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring-${environment}'
  params: {
    name: resourceSuffix
    location: location
  }
}

// Cosmos DB
module cosmosDb 'modules/cosmosDb.bicep' = {
  name: 'cosmosdb-${environment}'
  params: {
    name: resourceSuffix
    location: location
  }
}

// Azure OpenAI
module openAi 'modules/openAi.bicep' = {
  name: 'openai-${environment}'
  params: {
    name: resourceSuffix
    location: location
    deploymentName: openAiDeploymentName
  }
}

// Function App
module functionApp 'modules/functionApp.bicep' = {
  name: 'functionapp-${environment}'
  params: {
    name: resourceSuffix
    location: location
    appInsightsInstrumentationKey: monitoring.outputs.instrumentationKey
    appInsightsConnectionString: monitoring.outputs.connectionString
    cosmosDbEndpoint: cosmosDb.outputs.endpoint
    cosmosDbDatabaseName: cosmosDb.outputs.databaseName
    cosmosDbContainerName: cosmosDb.outputs.containerName
    openAiEndpoint: openAi.outputs.endpoint
    openAiDeploymentName: openAiDeploymentName
    botAppId: botAppId
    botAppPassword: botAppPassword
    graphClientId: graphClientId
    graphClientSecret: graphClientSecret
    graphTenantId: graphTenantId
  }
}

// Bot Service
module botService 'modules/botService.bicep' = {
  name: 'botservice-${environment}'
  params: {
    name: resourceSuffix
    location: location
    appId: botAppId
    appPassword: botAppPassword
    functionAppEndpoint: functionApp.outputs.endpoint
  }
}

output functionAppName string = functionApp.outputs.name
output functionAppEndpoint string = functionApp.outputs.endpoint
output cosmosDbEndpoint string = cosmosDb.outputs.endpoint
output openAiEndpoint string = openAi.outputs.endpoint
output botServiceName string = botService.outputs.name
