// ============================================================================
// CacheScope — Phase 0 infrastructure.
//
// Provisions the full cost-optimised topology:
//   - Log Analytics + Application Insights (telemetry sink for OpenTelemetry)
//   - Container Apps managed environment
//   - Redis as a self-hosted container app (L3)      -> $0, no managed Redis
//   - Azure SQL, serverless with auto-pause (L4)      -> ~$0 when idle
//   - API container app pulling its image from GHCR    -> $0, no ACR
//
// Deploy:
//   az deployment group create -g <rg> -f infra/main.bicep \
//     -p infra/main.parameters.json \
//     -p sqlAdminPassword=<secret> ghcrUsername=<user> ghcrToken=<pat> containerImage=<img>
// ============================================================================

@description('Deployment region.')
param location string = resourceGroup().location

@description('Short name stem for all resources.')
param appName string = 'cachescope'

@description('Container image for the API, e.g. ghcr.io/<user>/cachescope-host:latest')
param containerImage string

@description('GHCR username (your GitHub handle). Only needed for a private image.')
param ghcrUsername string = ''

@description('GHCR PAT with read:packages. Leave empty when the image is public.')
@secure()
param ghcrToken string = ''

@description('Azure SQL administrator login.')
param sqlAdminLogin string = 'cachescopeadmin'

@description('Azure SQL administrator password.')
@secure()
param sqlAdminPassword string

var ghcrConfigured = !empty(ghcrToken)
var suffix = uniqueString(resourceGroup().id)
var redisAppName = '${appName}-redis'
var apiAppName = '${appName}-api'
var sqlServerName = '${appName}-sql-${suffix}'
var sqlDbName = 'CacheScope'

// ---------------------------------------------------------------------------
// Observability: Log Analytics + workspace-based Application Insights.
// ---------------------------------------------------------------------------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appName}-logs-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-ai-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------------------------------------------------------------------------
// Container Apps environment.
// ---------------------------------------------------------------------------
resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${appName}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// ---------------------------------------------------------------------------
// L3 — self-hosted Redis. Internal TCP ingress; reachable by other apps in the
// environment at <redisAppName>:6379. Pure cache: no persistence, restarts read
// as an "L3 cold cache". Scales 1..1 (a cache that scales to zero is useless).
// ---------------------------------------------------------------------------
resource redis 'Microsoft.App/containerApps@2024-03-01' = {
  name: redisAppName
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: {
        external: false
        transport: 'tcp'
        targetPort: 6379
        exposedPort: 6379
      }
    }
    template: {
      containers: [
        {
          name: 'redis'
          image: 'redis:7-alpine'
          command: [ 'redis-server', '--save', '', '--appendonly', 'no' ]
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// ---------------------------------------------------------------------------
// L4 — Azure SQL, serverless with auto-pause after 60 min idle. You pay storage
// (pennies) while paused; the first query after a pause triggers a resume.
// ---------------------------------------------------------------------------
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Allow other Azure services (the container app) to reach the SQL server.
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: {
    name: 'GP_S_Gen5_1'   // General Purpose, serverless, 1 vCore max
    tier: 'GeneralPurpose'
  }
  properties: {
    autoPauseDelay: 60                 // minutes idle before auto-pause
    minCapacity: json('0.5')           // vCores when active
    maxSizeBytes: 2147483648           // 2 GB — plenty for the demo dataset
    zoneRedundant: false
  }
}

// ---------------------------------------------------------------------------
// API container app. Pulls from GHCR, exports telemetry to App Insights,
// reaches Redis and SQL over the environment's internal network.
// ---------------------------------------------------------------------------
resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
      }
      // Only attach GHCR credentials for a private image; a public image pulls anonymously.
      registries: ghcrConfigured ? [
        {
          server: 'ghcr.io'
          username: ghcrUsername
          passwordSecretRef: 'ghcr-token'
        }
      ] : []
      secrets: concat(ghcrConfigured ? [
        { name: 'ghcr-token', value: ghcrToken }
      ] : [], [
        { name: 'sql-connection', value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDbName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;' }
        { name: 'appinsights-connection', value: appInsights.properties.ConnectionString }
      ])
    }
    template: {
      containers: [
        {
          name: 'host'
          image: containerImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection' }
            { name: 'ConnectionStrings__Redis', value: '${redisAppName}:6379' }
            { name: 'ConnectionStrings__Sql', secretRef: 'sql-connection' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 3 }   // scale-to-zero when idle
    }
  }
}

output apiFqdn string = api.properties.configuration.ingress.fqdn
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output appInsightsConnectionString string = appInsights.properties.ConnectionString
