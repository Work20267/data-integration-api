using 'main.bicep'

param namePrefix = 'di'
param envName = 'dev'

// Azure for Students limits deployments to koreacentral, indonesiacentral,
// indiasouthcentral, polandcentral and uaenorth. Application Insights is not
// offered in indiasouthcentral, so uaenorth is the nearest of the four regions
// that support every service in this template. Overrides resourceGroup().location.
param location = 'uaenorth'

param appServicePlanSku = 'B1'

// Entra administrator for the SQL server (az ad signed-in-user show).
param sqlAdminObjectId = '5f81d1a8-4801-4cb5-8360-b8aa8dc4ffc8'
param sqlAdminLogin = 'work36451_gmail.com#EXT#@work36451gmail.onmicrosoft.com'
