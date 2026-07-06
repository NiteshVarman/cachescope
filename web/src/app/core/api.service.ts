import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { APP_CONFIG } from './config';
import { TrafficConfig, TrafficRunStatus } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(APP_CONFIG);

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
