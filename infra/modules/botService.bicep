@description('Resource name suffix')
param name string

@description('Azure region')
param location string

@description('Bot App ID')
@secure()
param appId string

@description('Bot App Password')
@secure()
param appPassword string

@description('Function App messaging endpoint')
param functionAppEndpoint string

var botName = 'bot-${name}'

resource botService 'Microsoft.BotService/botServices@2022-09-15' = {
  name: botName
  location: 'global'
  kind: 'azurebot'
  sku: {
    name: 'S1'
  }
  properties: {
    displayName: 'DigiSandra Scheduling Agent'
    description: 'Azure AI Scheduling Agent voor Microsoft Teams'
    endpoint: functionAppEndpoint
    msaAppId: appId
    msaAppType: 'SingleTenant'
    tenantId: tenant().tenantId
    schemaTransformationVersion: '1.3'
  }
}

resource teamsChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: botService
  name: 'MsTeamsChannel'
  location: 'global'
  properties: {
    channelName: 'MsTeamsChannel'
    properties: {
      isEnabled: true
    }
  }
}

output name string = botService.name
output botId string = botService.id
