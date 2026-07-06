import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { ApiService } from '../core/api.service';
import { CachePolicySnapshot, EXPIRATION_MODES, WRITE_STRATEGIES } from '../core/models';

@Component({
  selector: 'app-cache-control-panel',
  imports: [
    FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatSelectModule,
  ],
  templateUrl: './cache-control-panel.html',
  styleUrl: './cache-control-panel.scss',
})
export class CacheControlPanel implements OnInit {
  private readonly api = inject(ApiService);

  readonly writeStrategies = WRITE_STRATEGIES;
  readonly expirationModes = EXPIRATION_MODES;

  readonly policy = signal<CachePolicySnapshot | null>(null);
  readonly targetId = signal(42);
  readonly lastResult = signal<string>('');
  readonly busy = signal(false);

  async ngOnInit(): Promise<void> {
    this.policy.set(await this.api.getPolicy());
  }

  patch<K extends keyof CachePolicySnapshot>(key: K, value: CachePolicySnapshot[K]): void {
    const p = this.policy();
    if (p) this.policy.set({ ...p, [key]: value });
  }

  async applyPolicy(): Promise<void> {
    const p = this.policy();
    if (!p) return;
    this.policy.set(await this.api.updatePolicy(p));
    this.lastResult.set('Cache policy updated.');
  }

  private async run(label: string, action: () => Promise<{ message: string }>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    try {
      const r = await action();
      this.lastResult.set(r.message);
    } catch {
      this.lastResult.set(`${label} failed.`);
    } finally {
      this.busy.set(false);
    }
  }

  clearMemory() { return this.run('Clear memory', () => this.api.clearMemory()); }
  clearRedis() { return this.run('Clear Redis', () => this.api.clearRedis()); }
  warmMemory() { return this.run('Warm memory', () => this.api.warmMemory()); }
  warmRedis() { return this.run('Warm Redis', () => this.api.warmRedis()); }
  flushAll() { return this.run('Flush all', () => this.api.flushAll()); }
  purgeCloudflare() { return this.run('Purge Cloudflare', () => this.api.purgeCloudflare()); }
  expire() { return this.run('Expire', () => this.api.expireProduct(this.targetId())); }
  invalidate() { return this.run('Invalidate', () => this.api.invalidateProduct(this.targetId())); }
}
