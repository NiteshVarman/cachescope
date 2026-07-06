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
}
