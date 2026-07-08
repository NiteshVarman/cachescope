import { Injectable, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { APP_CONFIG } from './config';
import { LiveStatsSnapshot, RequestTrace, TrafficRunStatus } from './models';

const MAX_STREAM = 500; // cap the in-memory stream so the DOM stays light under load

@Injectable({ providedIn: 'root' })
export class SignalrService {
  private readonly config = inject(APP_CONFIG);
  private connection?: signalR.HubConnection;

  readonly connected = signal(false);
  readonly stats = signal<LiveStatsSnapshot | null>(null);
  readonly traces = signal<RequestTrace[]>([]);
  readonly trafficStatus = signal<TrafficRunStatus | null>(null);

  /** When paused, incoming traces still update stats but are not appended to the visible stream. */
  readonly paused = signal(false);

  async start(): Promise<void> {
    if (this.connection) {
      return;
    }

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(this.config.hubUrl)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.connection.on('ReceiveStats', (snapshot: LiveStatsSnapshot) => this.stats.set(snapshot));
    this.connection.on('ReceiveTraces', (batch: RequestTrace[]) => this.onTraces(batch));
    this.connection.on('ReceiveTrafficStatus', (status: TrafficRunStatus) => this.trafficStatus.set(status));
    // Timeline is consumed via the /api/analytics poll; register a no-op so SignalR
    // doesn't log "No client method 'ReceiveTimeline'" every second.
    this.connection.on('ReceiveTimeline', () => { /* intentionally ignored */ });

    this.connection.onreconnected(() => this.connected.set(true));
    this.connection.onclose(() => this.connected.set(false));

    await this.connectWithRetry(this.connection);
  }

  // withAutomaticReconnect only retries *dropped* connections, not the initial connect.
  // On a scale-to-zero backend the first attempt can fail while the container wakes, so
  // retry the initial connect with capped exponential backoff until it comes up.
  private async connectWithRetry(conn: signalR.HubConnection, attempt = 0): Promise<void> {
    try {
      await conn.start();
      this.connected.set(true);
    } catch {
      this.connected.set(false);
      const delay = Math.min(1000 * 2 ** attempt, 15000);
      setTimeout(() => void this.connectWithRetry(conn, attempt + 1), delay);
    }
  }

  private onTraces(batch: RequestTrace[]): void {
    if (this.paused() || batch.length === 0) {
      return;
    }
    // Newest first; keep only the most recent MAX_STREAM.
    const merged = [...batch.slice().reverse(), ...this.traces()];
    this.traces.set(merged.slice(0, MAX_STREAM));
  }

  clearStream(): void {
    this.traces.set([]);
  }

  togglePaused(): void {
    this.paused.update((p) => !p);
  }
}
