@description('Base name shared by all resources, e.g. "di-dev".')
param baseName string

param location string

@description('Resource id of the App Service these alerts watch.')
param appServiceId string

@description('Resource id of the Application Insights component the log alert queries.')
param appInsightsId string

@description('Optional. Email to notify. Left empty, alerts still fire and are visible in the portal; nobody is paged.')
param alertEmail string = ''

@description('Dead letters in an hour above which the pipeline is considered unhealthy.')
param deadLetterThreshold int = 500

var hasEmail = !empty(alertEmail)

// Action groups are global, not regional, regardless of where the app lives.
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-${baseName}'
  location: 'global'
  properties: {
    groupShortName: take(replace(baseName, '-', ''), 12)
    enabled: true
    emailReceivers: hasEmail
      ? [
          {
            name: 'ops'
            emailAddress: alertEmail
            // Common alert schema keeps the payload stable across alert types,
            // so a downstream handler does not need a parser per alert.
            useCommonAlertSchema: true
          }
        ]
      : []
  }
}

// Availability. A handful of 5xx is noise on a free tier that cold-starts;
// a sustained run of them is not.
resource serverErrors 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-${baseName}-5xx'
  location: 'global'
  properties: {
    description: 'App Service returned server errors. Check /health, then the exception trace in Application Insights.'
    severity: 2
    enabled: true
    scopes: [appServiceId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Http5xx'
          metricNamespace: 'Microsoft.Web/sites'
          metricName: 'Http5xx'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    autoMitigate: true
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// Data quality. Dead letters are normal in small numbers — a new sensor model
// produces a handful. A sudden run of them means either the upstream changed
// shape or a mapping rule was broken, and both need a human.
resource deadLetterSpike 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-${baseName}-deadletters'
  location: location
  properties: {
    displayName: 'Dead letters above threshold'
    description: 'Transformation is rejecting an unusual number of rows. Check GET /api/v1/dead-letter/summary first: one error code means a config gap, several means the upstream changed.'
    severity: 3
    enabled: true
    evaluationFrequency: 'PT15M'
    windowSize: 'PT1H'
    scopes: [appInsightsId]
    criteria: {
      allOf: [
        {
          query: '''
customMetrics
| where name == "di.transform.rows_dead_lettered"
| summarize DeadLettered = sum(valueSum)
'''
          timeAggregation: 'Total'
          metricMeasureColumn: 'DeadLettered'
          operator: 'GreaterThan'
          threshold: deadLetterThreshold
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    autoMitigate: true
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

output actionGroupId string = actionGroup.id
output serverErrorAlertName string = serverErrors.name
output deadLetterAlertName string = deadLetterSpike.name
