import { Component, Input } from '@angular/core';
import { DatePipe } from '@angular/common';
import type { UsageLogEntry } from '../../../models/usage.models';

// is_estimated is always shown, never hidden behind identical styling to
// a measured row - Gemini's embedContent calls (search-service,
// ingestion-resx) never return real token usage, so those rows can only
// ever be an estimate, and presenting an estimate identically to a
// measured fact would break the fidelity principle this whole feature is
// built on (docs/log-dashboard.md section 0/8).
@Component({
  selector: 'app-usage-log-table',
  standalone: true,
  imports: [DatePipe],
  template: `
    <div class="table-wrapper border-border">
      <table class="usage-table">
        <thead>
          <tr class="text-muted">
            <th>Data/ora</th>
            <th>Servizio</th>
            <th>Operazione</th>
            <th>Modello</th>
            <th>Token</th>
            <th>Costo (USD)</th>
            <th>Sessione</th>
          </tr>
        </thead>
        <tbody>
          @for (entry of entries; track entry.id) {
            <tr class="text-foreground">
              <td>{{ entry.occurredAt | date: 'dd/MM/yy HH:mm:ss' }}</td>
              <td>{{ entry.serviceName }}</td>
              <td class="text-muted">{{ entry.operation ?? '—' }}</td>
              <td>{{ entry.model }}</td>
              <td>
                {{ entry.totalTokens ?? '—' }}
                @if (entry.isEstimated) {
                  <span class="estimated-badge text-muted" title="Stimato dalla lunghezza del testo - Gemini non restituisce il conteggio reale dei token per le chiamate embedContent">~stima</span>
                }
              </td>
              <td>{{ entry.costUsd != null ? ('$' + entry.costUsd.toFixed(6)) : '—' }}</td>
              <td class="text-muted session-cell">{{ entry.sessionId ?? '—' }}</td>
            </tr>
          }
          @if (entries.length === 0) {
            <tr>
              <td colspan="7" class="text-muted empty-row">Nessuna chiamata registrata.</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styleUrl: './usage-log-table.component.css',
})
export class UsageLogTableComponent {
  @Input({ required: true }) entries: UsageLogEntry[] = [];
}
