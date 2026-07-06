import { Component, OnInit, signal } from '@angular/core';
import { KpiCardComponent } from '../kpi-card/kpi-card.component';
import { UsageChartComponent } from '../usage-chart/usage-chart.component';
import { UsageLogTableComponent } from '../usage-log-table/usage-log-table.component';
import { UsageApiService } from '../../../services/usage-api.service';
import type { UsageLogEntry, UsageSummary, UsageTimeSeriesPoint } from '../../../models/usage.models';

// The usage dashboard page (routed at /usage, see app.routes.ts) - see
// docs/log-dashboard.md section 6. All numbers shown here are exactly
// what search-service's /api/usage/* endpoints return, themselves exact
// SQL aggregates over gemini_usage_log - nothing is recomputed or
// estimated in this component.
@Component({
  selector: 'app-usage-dashboard',
  standalone: true,
  imports: [KpiCardComponent, UsageChartComponent, UsageLogTableComponent],
  template: `
    <div class="usage-page bg-background text-foreground">
      <h1 class="page-title">Utilizzo Gemini</h1>

      @if (error()) {
        <div class="error-banner border-border text-foreground">{{ error() }}</div>
      } @else if (loading()) {
        <div class="loading-state text-muted">Caricamento...</div>
      } @else {
        <div class="kpi-row">
          <app-kpi-card label="Spesa totale" [value]="'$' + (summary()?.totalCostUsd ?? 0).toFixed(4)" />
          <app-kpi-card label="Token totali" [value]="(summary()?.totalTokens ?? 0).toLocaleString('it-IT')" />
          <app-kpi-card label="Chiamate totali" [value]="(summary()?.totalCalls ?? 0).toLocaleString('it-IT')" />
        </div>

        <section class="chart-section bg-surface border-border">
          <h2 class="section-title text-muted">Andamento dei costi (ultimi 7 giorni)</h2>
          <app-usage-chart [points]="timeSeries()" />
        </section>

        <section class="breakdown-row">
          <div class="breakdown-card bg-surface border-border">
            <h2 class="section-title text-muted">Per modello</h2>
            @for (row of summary()?.byModel ?? []; track row.key) {
              <div class="breakdown-line text-foreground">
                <span>{{ row.key }}</span>
                <span>{{ '$' + row.costUsd.toFixed(4) }} · {{ row.calls }} chiamate</span>
              </div>
            }
          </div>
          <div class="breakdown-card bg-surface border-border">
            <h2 class="section-title text-muted">Per servizio</h2>
            @for (row of summary()?.byService ?? []; track row.key) {
              <div class="breakdown-line text-foreground">
                <span>{{ row.key }}</span>
                <span>{{ '$' + row.costUsd.toFixed(4) }} · {{ row.calls }} chiamate</span>
              </div>
            }
          </div>
        </section>

        <section class="log-section">
          <h2 class="section-title text-muted">Registro chiamate</h2>
          <app-usage-log-table [entries]="logEntries()" />
        </section>
      }
    </div>
  `,
  styleUrl: './usage-dashboard.component.css',
})
export class UsageDashboardComponent implements OnInit {
  readonly summary = signal<UsageSummary | null>(null);
  readonly timeSeries = signal<UsageTimeSeriesPoint[]>([]);
  readonly logEntries = signal<UsageLogEntry[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  constructor(private readonly api: UsageApiService) {}

  ngOnInit(): void {
    this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const from = new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString();
      const [summary, timeSeries, logs] = await Promise.all([
        this.api.getSummary(),
        this.api.getTimeSeries('day', from),
        this.api.getLogs({}, 0, 50),
      ]);
      this.summary.set(summary);
      this.timeSeries.set(timeSeries);
      this.logEntries.set(logs.entries);
    } catch (err) {
      this.error.set(err instanceof Error ? err.message : 'Errore sconosciuto');
    } finally {
      this.loading.set(false);
    }
  }
}
