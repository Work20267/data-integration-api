using 'main.bicep'

param namePrefix = 'di'
param envName = 'dev'
param appServicePlanSku = 'F1'

// Fill both from:  az ad signed-in-user show --query "{id:id, upn:userPrincipalName}" -o json
param sqlAdminObjectId = ''
param sqlAdminLogin = ''
