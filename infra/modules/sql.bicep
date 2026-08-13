@description('Base name shared by all resources, e.g. "di-dev".')
param baseName string

@description('Deterministic suffix for the globally-unique server name.')
param suffix string

param location string

@description('Entra object ID of the server administrator.')
param adminObjectId string

@description('Entra login name of the server administrator.')
param adminLogin string

// Entra-only authentication: there is no SQL admin password to leak.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${baseName}-${suffix}'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: adminLogin
      sid: adminObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// 0.0.0.0-0.0.0.0 is the special rule meaning "allow Azure services", which is
// how the App Service reaches the server without a VNet.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Serverless General Purpose on the free offer: 100k vCore-seconds + 32 GB/month.
// AutoPause on exhaustion keeps the bill at zero rather than overflowing to paid.
resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'sqldb-${baseName}'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    zoneRedundant: false
    collation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

output serverName string = sqlServer.name
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
