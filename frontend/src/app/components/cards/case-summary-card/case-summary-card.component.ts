import { Component, EventEmitter, Input, Output } from '@angular/core';
import { StarRatingComponent } from '../../ui/star-rating/star-rating.component';
import { DtcBadgeComponent } from '../../ui/dtc-badge/dtc-badge.component';
import type { CaseSummary } from '../../../models/chat.models';

// Compact card for the multi-document selection list (2+ results).
// Shows only the fields needed to distinguish documents at a glance:
// badge, titolo, reliability stars, impianto, dispositivo, dtcCodes.
// anomalia / causa / intervento / procedura / nota are withheld — those
// appear only in the expanded full RepairCaseCardComponent.
// Badge CSS is intentionally identical to car-card's .car-badge so the
// two selection UIs feel like one system.
@Component({
  selector: 'app-case-summary-card',
  standalone: true,
  imports: [StarRatingComponent, DtcBadgeComponent],
  template: `
    <button
      type="button"
      class="case-summary-card bg-card-surface border-border hover:border-accent hover:bg-foreground/8"
      (click)="select.emit()"
    >
      <span class="car-badge">{{ badge }}</span>
      <div class="cs-titolo text-accent">{{ caseSummary.titolo }}</div>
      <div class="cs-stars">
        <app-star-rating [reliability]="caseSummary.reliability" />
      </div>
      @if (caseSummary.impianto) {
        <div class="cs-system text-muted">{{ caseSummary.impianto }}</div>
      }
      @if (caseSummary.dispositivo) {
        <div class="cs-device text-foreground">{{ caseSummary.dispositivo }}</div>
      }
      @if (caseSummary.dtcCodes.length > 0) {
        <div class="cs-dtcs">
          @for (dtc of caseSummary.dtcCodes; track dtc.code) {
            <app-dtc-badge [code]="dtc.code" />
          }
        </div>
      }
    </button>
  `,
  styleUrl: './case-summary-card.component.css',
})
export class CaseSummaryCardComponent {
  @Input({ required: true }) caseSummary!: CaseSummary;
  @Input() badge = 0;
  @Output() select = new EventEmitter<void>();
}
