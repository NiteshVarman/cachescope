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

    this.connection.onreconnected(() => this.connected.set(true));
    this.connection.onclose(() => this.connected.set(false));

    await this.connection.start();
    this.connected.set(true);
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
