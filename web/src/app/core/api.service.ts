import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { APP_CONFIG } from './config';
import {
  AnalyticsSnapshot, CacheOpResult, CachePolicySnapshot, RequestDetail,
  StampedeResult, TrafficConfig, TrafficRunStatus,
} from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(APP_CONFIG);

  getAnalytics(): Promise<AnalyticsSnapshot> {
    return firstValueFrom(this.http.get<AnalyticsSnapshot>(`${this.config.apiBaseUrl}/api/analytics/`));
  }

  resetAnalytics(): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.config.apiBaseUrl}/api/analytics/reset`, {}));
  }

  // ---- cache policy ----
  getPolicy(): Promise<CachePolicySnapshot> {
    return firstValueFrom(this.http.get<CachePolicySnapshot>(`${this.config.apiBaseUrl}/api/policy/`));
  }

  updatePolicy(patch: Partial<CachePolicySnapshot>): Promise<CachePolicySnapshot> {
    return firstValueFrom(this.http.put<CachePolicySnapshot>(`${this.config.apiBaseUrl}/api/policy/`, patch));
  }

  // ---- cache operations ----
  private op(path: string): Promise<CacheOpResult> {
    return firstValueFrom(this.http.post<CacheOpResult>(`${this.config.apiBaseUrl}/api/cache/${path}`, {}));
  }
  clearMemory() { return this.op('clear-memory'); }
  clearRedis() { return this.op('clear-redis'); }
  warmMemory() { return this.op('warm-memory'); }
  warmRedis() { return this.op('warm-redis'); }
  flushAll() { return this.op('flush'); }
  purgeCloudflare() { return this.op('purge-cloudflare'); }
  expireProduct(id: number) { return this.op(`expire/${id}`); }
  invalidateProduct(id: number) { return this.op(`invalidate/${id}`); }

  runStampede(hotKeyId: number, concurrency: number): Promise<StampedeResult> {
    return firstValueFrom(
      this.http.post<StampedeResult>(
        `${this.config.apiBaseUrl}/api/stampede?hotKeyId=${hotKeyId}&concurrency=${concurrency}`, {}));
  }

  getTraceDetail(correlationId: string): Promise<RequestDetail> {
    return firstValueFrom(
      this.http.get<RequestDetail>(`${this.config.apiBaseUrl}/api/traces/${correlationId}`));
  }

  startTraffic(cfg: TrafficConfig): Promise<{ runId: string }> {
    return firstValueFrom(this.http.post<{ runId: string }>(`${this.config.apiBaseUrl}/api/traffic/start`, cfg));
  }

  stopTraffic(): Promise<TrafficRunStatus> {
    return firstValueFrom(this.http.post<TrafficRunStatus>(`${this.config.apiBaseUrl}/api/traffic/stop`, {}));
  }

  trafficStatus(): Promise<TrafficRunStatus> {
    return firstValueFrom(this.http.get<TrafficRunStatus>(`${this.config.apiBaseUrl}/api/traffic/status`));
  }

  getProductIds(): Promise<number[]> {
    return firstValueFrom(this.http.get<number[]>(`${this.config.apiBaseUrl}/api/products/`));
  }

  getProduct(id: number): Promise<unknown> {
    return firstValueFrom(this.http.get(`${this.config.apiBaseUrl}/api/products/${id}`));
  }

  /**
   * A minimal manual traffic burst so Phase 2 is demonstrable before the full
   * Traffic Generator (Phase 3). Fires `count` GETs across the given product ids.
   */
  async burst(ids: number[], count: number): Promise<void> {
    const requests: Promise<unknown>[] = [];
    for (let i = 0; i < count; i++) {
      const id = ids[i % ids.length];
      requests.push(this.getProduct(id).catch(() => undefined));
    }
    await Promise.all(requests);
  }

  /**
   * Measures L1 (browser cache). L1 hits never leave the browser, so the server
   * can't see them — only the client can. We warm a small set of products, then
   * re-request them and inspect the Resource Timing API: an entry with
   * transferSize === 0 (with a body) or deliveryType 'cache' was served from the
   * browser's HTTP cache. Requires the API's Timing-Allow-Origin header (cross-origin).
   */
  async probeBrowserCache(ids: number[]): Promise<{ total: number; cacheHits: number }> {
    const sample = ids.slice(0, 10);
    if (sample.length === 0) return { total: 0, cacheHits: 0 };
    const url = (id: number) => `${this.config.apiBaseUrl}/api/products/${id}`;

    // Raw fetch (default cache mode) so the browser HTTP cache is used predictably.
    // Phase 1 — warm the browser cache (responses are Cache-Control: max-age=30).
    await Promise.all(sample.map((id) => fetch(url(id)).catch(() => undefined)));
    await new Promise((r) => setTimeout(r, 250));

    // Phase 2 — clear the timing buffer, then re-request the same ids and measure.
    performance.clearResourceTimings();
    await Promise.all(sample.map((id) => fetch(url(id)).catch(() => undefined)));
    await new Promise((r) => setTimeout(r, 300));

    const entries = performance.getEntriesByType('resource') as PerformanceResourceTiming[];
    let total = 0;
    let cacheHits = 0;
    for (const e of entries) {
      if (!e.name.includes('/api/products/')) continue;
      total++;
      // transferSize === 0 (with a real body) ⇒ served from the browser cache; deliveryType
      // 'cache' is the newer explicit signal. Both require Timing-Allow-Origin cross-origin.
      const fromCache =
        (e as unknown as { deliveryType?: string }).deliveryType === 'cache' ||
        (e.transferSize === 0 && e.decodedBodySize > 0);
      if (fromCache) cacheHits++;
    }
    return { total, cacheHits };
  }
}
