using './main.bicep'

param location = 'australiaeast'
param env = 'prod'
param auth0Domain = 'auth.pantrypunk.ai'
param auth0Audience = 'https://api.pantrypunk.ai'
param appServicePlanSku = 'B1'
param logRetentionDays = 30
