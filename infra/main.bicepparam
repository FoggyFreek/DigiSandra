using 'main.bicep'

param environment = 'dev'
param botAppId = readEnvironmentVariable('BOT_APP_ID')
param botAppPassword = readEnvironmentVariable('BOT_APP_PASSWORD')
param graphClientId = readEnvironmentVariable('GRAPH_CLIENT_ID')
param graphClientSecret = readEnvironmentVariable('GRAPH_CLIENT_SECRET')
param graphTenantId = readEnvironmentVariable('GRAPH_TENANT_ID')
