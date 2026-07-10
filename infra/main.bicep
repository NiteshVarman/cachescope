// ============================================================================
// CacheScope — Phase 0 infrastructure.
//
// Provisions the full cost-optimised topology:
//   - Log Analytics + Application Insights (telemetry sink for OpenTelemetry)
//   - Container Apps managed environment
//   - Redis as a self-hosted container app (L3)      -> $0, no managed Redis
//   - L4 is embedded SQLite inside the API container   -> $0, no managed database
//   - API container app pulling its image from GHCR    -> $0, no ACR
//
// Deploy:
//   az deployment group create -g <rg> -f infra/main.bicep \
//     -p infra/main.parameters.json \
//     -p ghcrUsername=<user> ghcrToken=<pat> containerImage=<img>
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

@description('Artificial DB latency (ms) so the L2/L3-vs-L4 gap and the stampede demo are visible. Demo knob.')
param simulatedQueryLatencyMs int = 45

@description('SQLite connection string for L4 (a file inside the container; ephemeral, re-seeded on boot).')
param sqlConnectionString string = 'Data Source=/tmp/cachescope.db'

var ghcrConfigured = !empty(ghcrToken)
var suffix = uniqueString(resourceGroup().id)
var redisAppName = '${appName}-redis'
var apiAppName = '${appName}-api'

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
// L4 — embedded SQLite. There is NO managed database resource: the source of
// truth is a SQLite file inside the API container, created and seeded on boot.
// This keeps the database cost at $0 (a managed DB, or a self-hosted DB
// container that can't scale to zero, would incur ongoing charges).
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// API container app. Pulls from GHCR, exports telemetry to App Insights,
// reaches Redis over the environment's internal network. L4 is in-process SQLite.
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
            { name: 'ConnectionStrings__Sql', value: sqlConnectionString }
            { name: 'Database__SimulatedQueryLatencyMs', value: string(simulatedQueryLatencyMs) }
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
      // Single replica: CacheScope's stats/traffic/stream state are in-process singletons,
      // so a coherent dashboard needs one instance. (Scaling out would require Azure SignalR
      // + a Redis-backed store.) minReplicas 0 keeps scale-to-zero cost.
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
}

output apiFqdn string = api.properties.configuration.ingress.fqdn
output appInsightsConnectionString string = appInsights.properties.ConnectionString
