import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { AnalyticsPanel } from './analytics/analytics-panel';
import { CacheControlPanel } from './cache-control/cache-control-panel';
import { StampedePanel } from './stampede/stampede-panel';
import { ApiService } from './core/api.service';
import { SignalrService } from './core/signalr.service';
import {
  CacheLayer, EdgeStatsSnapshot, KEY_SELECTIONS, LAYER_COLOR, LAYER_ORDER,
  RequestDetail, RequestTrace, TRAFFIC_PATTERNS, TrafficConfig,
} from './core/models';

@Component({
  selector: 'app-root',
  imports: [
    AnalyticsPanel,
    CacheControlPanel,
    StampedePanel,
    DecimalPipe,
    FormsModule,
    MatToolbarModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatProgressBarModule,
    MatTooltipModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  readonly hub = inject(SignalrService);

  // Top chips are the server-measured layers only (L2/L3/L4 + client-probed Browser L1).
  // L0 (Cloudflare) is shown separately as an edge card — it's an out-of-band aggregate.
  readonly chipLayers = LAYER_ORDER.filter((l) => l !== 'Cloudflare');
  readonly layerColor = LAYER_COLOR;

  // L0 edge stats, polled from /api/analytics (aggregate, ~minutes delayed, via Cloudflare).
  readonly edge = signal<EdgeStatsSnapshot | null>(null);
  private edgeTimer?: ReturnType<typeof setInterval>;

  private productIds: number[] = [];
  readonly burstSize = signal(200);
  readonly bursting = signal(false);

  // L1 (browser cache) — measured client-side, since the server can't see browser-cache hits.
  readonly browserCache = signal<{ total: number; cacheHits: number } | null>(null);
  readonly probingL1 = signal(false);

  // ---- Traffic generator ----
  readonly patterns = TRAFFIC_PATTERNS;
  readonly keySelections = KEY_SELECTIONS;
  readonly config = signal<TrafficConfig>({
    pattern: 'Zipf',
    mode: 'Origin',
    totalRequests: 1000,
    requestsPerSecond: 200,
    durationSeconds: null,
    concurrency: 20,
    getPercentage: 100,
    keySelection: 'Random',
    hotKeyCount: 10,
  });

  readonly trafficRunning = computed(() => {
    const st = this.hub.trafficStatus()?.state;
    return st === 'Running' || st === 'Preparing';
  });

  patch<K extends keyof TrafficConfig>(key: K, value: TrafficConfig[K]): void {
    this.config.update((c) => ({ ...c, [key]: value }));
  }

  async startTraffic(): Promise<void> {
    try {
      await this.api.startTraffic(this.config());
    } catch {
      /* a 409 means a run is already active; the panel reflects state */
    }
  }

  async stopTraffic(): Promise<void> {
    await this.api.stopTraffic();
  }

  // Stream filters.
  readonly search = signal('');
  readonly layerFilter = signal<CacheLayer | 'All'>('All');

  readonly filteredTraces = computed<RequestTrace[]>(() => {
    const term = this.search().trim().toLowerCase();
    const layer = this.layerFilter();
    return this.hub.traces().filter((t) => {
      if (layer !== 'All' && t.servedBy !== layer) return false;
      if (term && !`${t.path} ${t.method} ${t.correlationId} #${t.requestId}`.toLowerCase().includes(term)) {
        return false;
      }
      return true;
    });
  });

  async ngOnInit(): Promise<void> {
    await this.hub.start();
    try {
      this.productIds = await this.api.getProductIds();
    } catch {
      this.productIds = Array.from({ length: 100 }, (_, i) => i + 1);
    }
    void this.refreshEdge();
    this.edgeTimer = setInterval(() => void this.refreshEdge(), 5000);
  }

  ngOnDestroy(): void {
    if (this.edgeTimer) clearInterval(this.edgeTimer);
  }

  private async refreshEdge(): Promise<void> {
    try {
      const a = await this.api.getAnalytics();
      this.edge.set(a.cloudflareEdge ?? null);
    } catch {
      /* analytics momentarily unavailable */
    }
  }

  layerCount(layer: CacheLayer): number {
    // L1 comes from the client-side probe, not the server (browser hits never reach it).
    if (layer === 'Browser') return this.browserCache()?.cacheHits ?? 0;
    const s = this.hub.stats();
    if (!s) return 0;
    switch (layer) {
      case 'Cloudflare': return s.cloudflareHits;
      case 'Memory': return s.memoryHits;
      case 'Redis': return s.redisHits;
      case 'Database': return s.databaseHits;
      default: return 0;
    }
  }

  async probeL1(): Promise<void> {
    if (this.probingL1() || this.productIds.length === 0) return;
    this.probingL1.set(true);
    try {
      this.browserCache.set(await this.api.probeBrowserCache(this.productIds));
    } finally {
      this.probingL1.set(false);
    }
  }

  async fireBurst(): Promise<void> {
    if (this.bursting() || this.productIds.length === 0) return;
    this.bursting.set(true);
    try {
      await this.api.burst(this.productIds, this.burstSize());
    } finally {
      this.bursting.set(false);
    }
  }

  statusClass(code: number): string {
    if (code >= 500) return 'status-err';
    if (code >= 400) return 'status-warn';
    if (code === 304) return 'status-304';
    return 'status-ok';
  }

  // ---- per-request detail (observability waterfall) ----
  readonly detail = signal<RequestDetail | null>(null);
  readonly detailError = signal('');

  async openDetail(t: RequestTrace): Promise<void> {
    this.detailError.set('');
    try {
      this.detail.set(await this.api.getTraceDetail(t.correlationId));
    } catch {
      this.detail.set(null);
      this.detailError.set('Trace detail is no longer available (it may have aged out of the buffer).');
    }
  }

  closeDetail(): void {
    this.detail.set(null);
    this.detailError.set('');
  }

  segmentWidth(ms: number): number {
    const d = this.detail();
    if (!d || d.segments.length === 0) return 0;
    const max = Math.max(...d.segments.map((s) => s.ms), 0.01);
    return (ms / max) * 100;
  }
}
