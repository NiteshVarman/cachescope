// Mirrors the CacheScope.Shared contracts serialized by the API (System.Text.Json web defaults → camelCase).

export type CacheLayer = 'Cloudflare' | 'Browser' | 'Memory' | 'Redis' | 'Database';
export type CacheOutcome = 'Hit' | 'Miss';

export interface RequestTrace {
  requestId: number;
  correlationId: string;
  timestamp: string;
  method: string;
  path: string;
  servedBy: CacheLayer;
  outcome: CacheOutcome;
  responseTimeMs: number;
  cfCacheStatus?: string | null;
  statusCode: number;
}

export interface LiveStatsSnapshot {
  totalRequests: number;
  cloudflareHits: number;
  browserHits: number;
  memoryHits: number;
  redisHits: number;
  databaseHits: number;
  failedRequests: number;
  cacheHitRatio: number;
  averageLatencyMs: number;
  peakLatencyMs: number;
  p50LatencyMs: number;
  p95LatencyMs: number;
  p99LatencyMs: number;
  cfCacheStatusCounts: Record<string, number>;
  edgeHitRatio: number;
}

export interface MetricsTimelinePoint {
  timestamp: string;
  requestsPerSecond: number;
  averageLatencyMs: number;
  cacheHitRatio: number;
  databaseQueriesPerSecond: number;
}

export interface AnalyticsSnapshot {
  stats: LiveStatsSnapshot;
  databaseQueriesExecuted: number;
  databaseQueriesPrevented: number;
  databaseAverageQueryTimeMs: number;
  timeline: MetricsTimelinePoint[];
}

// ---- Traffic generator contracts (mirror CacheScope.Shared.Traffic) ----
export type TrafficPattern =
  | 'ColdStart' | 'WarmCache' | 'Steady' | 'Burst' | 'HotKey'
  | 'RandomKeys' | 'Zipf' | 'CacheStampede' | 'BotTraffic' | 'Mixed';
export type KeySelectionMode = 'SingleHotKey' | 'TopNHotKeys' | 'Random' | 'Sequential';
export type TrafficMode = 'Origin' | 'Edge';
export type TrafficRunState = 'Idle' | 'Preparing' | 'Running' | 'Completed' | 'Cancelled' | 'Failed';

export interface TrafficConfig {
  pattern: TrafficPattern;
  mode: TrafficMode;
  totalRequests: number | null;
  requestsPerSecond: number | null;
  durationSeconds: number | null;
  concurrency: number;
  getPercentage: number;
  keySelection: KeySelectionMode;
  hotKeyCount: number;
}

export interface TrafficRunStatus {
  runId: string;
  state: TrafficRunState;
  pattern: TrafficPattern;
  mode: TrafficMode;
  targetTotal: number | null;
  completed: number;
  failed: number;
  pending: number;
  currentRps: number;
  averageLatencyMs: number;
  peakLatencyMs: number;
  elapsedMs: number;
  message?: string | null;
}

export const TRAFFIC_PATTERNS: TrafficPattern[] = [
  'ColdStart', 'WarmCache', 'Steady', 'Burst', 'HotKey',
  'RandomKeys', 'Zipf', 'CacheStampede', 'BotTraffic', 'Mixed',
];
export const KEY_SELECTIONS: KeySelectionMode[] = ['SingleHotKey', 'TopNHotKeys', 'Random', 'Sequential'];

// ---- Cache policy & operations (mirror CacheScope.Shared.Caching / .Operations) ----
export type ExpirationMode = 'Absolute' | 'Sliding';
export type WriteStrategy = 'CacheAside' | 'WriteThrough' | 'WriteBehind' | 'RefreshAhead';

export interface CachePolicySnapshot {
  memoryTtlSeconds: number;
  redisTtlSeconds: number;
  memoryExpiration: ExpirationMode;
  writeStrategy: WriteStrategy;
}

export interface CacheOpResult {
  operation: string;
  message: string;
  affected: number;
}

export interface StampedeScenario {
  protectionEnabled: boolean;
  concurrency: number;
  databaseQueries: number;
  durationMs: number;
}

export interface StampedeResult {
  hotKeyId: number;
  unprotected: StampedeScenario;
  protected: StampedeScenario;
}

export const WRITE_STRATEGIES: WriteStrategy[] = ['CacheAside', 'WriteThrough', 'WriteBehind', 'RefreshAhead'];
export const EXPIRATION_MODES: ExpirationMode[] = ['Absolute', 'Sliding'];

export const LAYER_ORDER: CacheLayer[] = ['Cloudflare', 'Browser', 'Memory', 'Redis', 'Database'];

// Colour per layer, reused by chips and (Phase 4) charts.
export const LAYER_COLOR: Record<CacheLayer, string> = {
  Cloudflare: '#f6821f',
  Browser: '#9c6ade',
  Memory: '#3ecf8e',
  Redis: '#d82c20',
  Database: '#5b8def',
};
