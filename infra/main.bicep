targetScope = 'resourceGroup'

@description('Short prefix for all resource names, e.g. "di".')
@minLength(2)
@maxLength(6)
param namePrefix string

@description('Environment discriminator.')
@allowed(['dev', 'prod'])
param envName string

@description('Azure region. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Entra object ID of the SQL Server Entra administrator. From: az ad signed-in-user show --query id -o tsv')
param sqlAdminObjectId string

@description('Entra login name (UPN or display name) of the SQL Server administrator.')
param sqlAdminLogin string

@description('App Service plan SKU. F1 is the free tier; B1 adds Always On and health checks.')
@allowed(['F1', 'B1'])
param appServicePlanSku string = 'F1'

@description('Optional. Email to notify on alerts. Empty still creates the alerts; nobody is paged.')
param alertEmail string = ''

@description('Dead letters in an hour above which the pipeline is considered unhealthy.')
@minValue(1)
param deadLetterThreshold int = 500

var baseName = '${namePrefix}-${envName}'

// Globally-unique names (SQL server, App Service) need a deterministic suffix.
var suffix = uniqueString(resourceGroup().id)

// The App Service authenticates to Azure SQL as this identity — no password anywhere.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${baseName}'
  location: location
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    baseName: baseName
    location: location
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    baseName: baseName
    suffix: suffix
    location: location
    adminObjectId: sqlAdminObjectId
    adminLogin: sqlAdminLogin
  }
}

module appService 'modules/appservice.bicep' = {
  name: 'appService'
  params: {
    baseName: baseName
    suffix: suffix
    location: location
    skuName: appServicePlanSku
    identityResourceId: identity.id
    identityClientId: identity.properties.clientId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
  }
}

// Monitoring is provisioned by the same template as the thing it watches, so an
// environment cannot exist without its alerts.
module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    baseName: baseName
    location: location
    appServiceId: appService.outputs.resourceId
    appInsightsId: monitoring.outputs.appInsightsResourceId
    alertEmail: alertEmail
    deadLetterThreshold: deadLetterThreshold
  }
}

output appServiceName string = appService.outputs.name
output appServiceUrl string = appService.outputs.url
output sqlServerName string = sql.outputs.serverName
output sqlServerFqdn string = sql.outputs.serverFqdn
output sqlDatabaseName string = sql.outputs.databaseName

output actionGroupId string = alerts.outputs.actionGroupId
output serverErrorAlertName string = alerts.outputs.serverErrorAlertName
output deadLetterAlertName string = alerts.outputs.deadLetterAlertName

// Needed to create the SQL contained user for the App Service identity.
output identityName string = identity.name
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
