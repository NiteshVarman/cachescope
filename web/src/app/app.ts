import { Component, OnInit, computed, inject, signal } from '@angular/core';
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
  CacheLayer, KEY_SELECTIONS, LAYER_COLOR, LAYER_ORDER,
  RequestTrace, TRAFFIC_PATTERNS, TrafficConfig,
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
export class App implements OnInit {
  private readonly api = inject(ApiService);
  readonly hub = inject(SignalrService);

  readonly layers = LAYER_ORDER;
  readonly layerColor = LAYER_COLOR;

  private productIds: number[] = [];
  readonly burstSize = signal(200);
  readonly bursting = signal(false);

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
  }

  layerCount(layer: CacheLayer): number {
    const s = this.hub.stats();
    if (!s) return 0;
    switch (layer) {
      case 'Cloudflare': return s.cloudflareHits;
      case 'Browser': return s.browserHits;
      case 'Memory': return s.memoryHits;
      case 'Redis': return s.redisHits;
      case 'Database': return s.databaseHits;
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
}
