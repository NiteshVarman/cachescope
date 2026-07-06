import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

import { ApiService } from '../core/api.service';
import { AnalyticsSnapshot, CacheLayer, LAYER_COLOR, LAYER_ORDER } from '../core/models';

interface Series {
  line: string;
  area: string;
  max: number;
}

@Component({
  selector: 'app-analytics-panel',
  imports: [DecimalPipe, MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './analytics-panel.html',
  styleUrl: './analytics-panel.scss',
})
export class AnalyticsPanel implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private timer?: ReturnType<typeof setInterval>;

  readonly snapshot = signal<AnalyticsSnapshot | null>(null);
  readonly layers = LAYER_ORDER;
  readonly layerColor = LAYER_COLOR;

  // Chart geometry.
  readonly chartW = 560;
  readonly chartH = 120;

  ngOnInit(): void {
    void this.refresh();
    this.timer = setInterval(() => void this.refresh(), 1000);
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }

  private async refresh(): Promise<void> {
    try {
      this.snapshot.set(await this.api.getAnalytics());
    } catch {
      /* backend may be momentarily unavailable */
    }
  }

  async reset(): Promise<void> {
    await this.api.resetAnalytics();
    await this.refresh();
  }

  // ---- derived chart data ----
  readonly layerRows = computed(() => {
    const s = this.snapshot()?.stats;
    if (!s) return [];
    const counts: Record<CacheLayer, number> = {
      Cloudflare: s.cloudflareHits,
      Browser: s.browserHits,
      Memory: s.memoryHits,
      Redis: s.redisHits,
      Database: s.databaseHits,
    };
    const total = Math.max(1, s.totalRequests);
    return this.layers.map((l) => ({
      layer: l,
      count: counts[l],
      pct: (counts[l] / total) * 100,
      color: this.layerColor[l],
    }));
  });

  // Donut background built from a conic-gradient of the layer distribution.
  readonly donutGradient = computed(() => {
    const rows = this.layerRows();
    const total = rows.reduce((a, r) => a + r.count, 0);
    if (total === 0) return 'conic-gradient(#2a3440 0 100%)';
    let acc = 0;
    const stops = rows
      .filter((r) => r.count > 0)
      .map((r) => {
        const start = (acc / total) * 100;
        acc += r.count;
        const end = (acc / total) * 100;
        return `${r.color} ${start}% ${end}%`;
      });
    return `conic-gradient(${stops.join(', ')})`;
  });

  readonly hitRatio = computed(() => (this.snapshot()?.stats.cacheHitRatio ?? 0) * 100);

  readonly gaugeGradient = computed(() => {
    const pct = this.hitRatio();
    return `conic-gradient(#3ecf8e 0 ${pct}%, #2a3440 ${pct}% 100%)`;
  });

  readonly latencySeries = computed(() =>
    this.buildSeries(this.snapshot()?.timeline.map((p) => p.averageLatencyMs) ?? []),
  );

  readonly rpsSeries = computed(() =>
    this.buildSeries(this.snapshot()?.timeline.map((p) => p.requestsPerSecond) ?? []),
  );

  private buildSeries(values: number[]): Series {
    const n = values.length;
    if (n === 0) return { line: '', area: '', max: 0 };
    const max = Math.max(1, ...values);
    const w = this.chartW;
    const h = this.chartH;
    const step = n > 1 ? w / (n - 1) : w;
    const pts = values.map((v, i) => {
      const x = n > 1 ? i * step : w / 2;
      const y = h - (v / max) * (h - 6) - 3;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    });
    const line = pts.join(' ');
    const area = `0,${h} ${line} ${(n > 1 ? (n - 1) * step : w / 2).toFixed(1)},${h}`;
    return { line, area, max };
  }

  cfEntries(): { status: string; count: number }[] {
    const cf = this.snapshot()?.stats.cfCacheStatusCounts ?? {};
    return Object.entries(cf).map(([status, count]) => ({ status, count }));
  }
}
