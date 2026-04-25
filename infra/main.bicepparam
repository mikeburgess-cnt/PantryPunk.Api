using './main.bicep'

param location = 'australiaeast'
param env = 'prod'
param auth0Domain = '<your-tenant>.au.auth0.com'
param auth0Audience = 'https://api.pantrypunk.ai'
param appServicePlanSku = 'B1'
param logRetentionDays = 30
