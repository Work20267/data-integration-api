@description('Base name shared by all resources, e.g. "di-dev".')
param baseName string

@description('Deterministic suffix for the globally-unique site name.')
param suffix string

param location string

@allowed(['F1', 'B1'])
param skuName string

param identityResourceId string
param identityClientId string
param appInsightsConnectionString string
param sqlServerFqdn string
param sqlDatabaseName string

// Always On and health checks are Basic-tier features; F1 must leave them off.
var isFreeTier = skuName == 'F1'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${baseName}'
  location: location
  kind: 'linux'
  sku: {
    name: skuName
    tier: isFreeTier ? 'Free' : 'Basic'
  }
  properties: {
    reserved: true // required for Linux
  }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${baseName}-${suffix}'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: !isFreeTier
      healthCheckPath: isFreeTier ? null : '/health'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          // DefaultAzureCredential needs this to pick the user-assigned identity.
          name: 'AZURE_CLIENT_ID'
          value: identityClientId
        }
        {
          name: 'ConnectionStrings__Default'
          value: 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${identityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
        }
      ]
    }
  }
}

output name string = app.name
output url string = 'https://${app.properties.defaultHostName}'
output defaultHostName string = app.properties.defaultHostName
