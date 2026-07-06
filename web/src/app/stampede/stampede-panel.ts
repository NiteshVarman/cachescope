import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { ApiService } from '../core/api.service';
import { StampedeResult } from '../core/models';

@Component({
  selector: 'app-stampede-panel',
  imports: [
    DecimalPipe, FormsModule, MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatProgressBarModule,
  ],
  templateUrl: './stampede-panel.html',
  styleUrl: './stampede-panel.scss',
})
export class StampedePanel {
  private readonly api = inject(ApiService);

  readonly hotKeyId = signal(1);
  readonly concurrency = signal(1000);
  readonly running = signal(false);
  readonly result = signal<StampedeResult | null>(null);
  readonly error = signal('');

  // The unprotected count is the visual maximum both bars scale against.
  private readonly maxQueries = computed(() => {
    const r = this.result();
    return r ? Math.max(1, r.unprotected.databaseQueries, r.protected.databaseQueries) : 1;
  });

  barWidth(queries: number): number {
    return (queries / this.maxQueries()) * 100;
  }

  async run(): Promise<void> {
    if (this.running()) return;
    this.running.set(true);
    this.error.set('');
    try {
      this.result.set(await this.api.runStampede(this.hotKeyId(), this.concurrency()));
    } catch {
      this.error.set('A stampede demo may already be running — try again in a moment.');
    } finally {
      this.running.set(false);
    }
  }
}
