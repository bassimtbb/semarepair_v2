import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CarSelectionListComponent } from '../../cards/car-selection-list/car-selection-list.component';
import { RepairCaseCardComponent } from '../../cards/repair-case-card/repair-case-card.component';
import { CaseSummaryCardComponent } from '../../cards/case-summary-card/case-summary-card.component';
import type { CarOption, CaseSummary, ChatMessage } from '../../../models/chat.models';

const BACK_LABELS: Record<string, string> = {
  it: '← Tutti i casi',
  en: '← All cases',
  fr: '← Tous les cas',
  pt: '← Todos os casos',
  es: '← Todos los casos',
};

@Component({
  selector: 'app-message-bubble',
  standalone: true,
  imports: [CarSelectionListComponent, RepairCaseCardComponent, CaseSummaryCardComponent],
  template: `
    <div class="bubble-row" [class.user]="message.role === 'user'">
      <div class="bubble bg-bubble border-border text-foreground" [class.user]="message.role === 'user'">
        @if (message.text) {
          <div class="text">{{ message.text }}</div>
        }

        @if (message.carMatches && message.carMatches.length > 0) {
          <app-car-selection-list [cars]="message.carMatches" (select)="selectCar.emit($event)" />
        }

        @if (message.cases && message.cases.length > 0) {
          @if (message.cases.length === 1) {
            <!-- Single result: always show the full card immediately -->
            <div class="cards">
              <app-repair-case-card [caseSummary]="message.cases[0]" />
            </div>
          } @else if (message.selectedCaseIndex != null) {
            <!-- Expanded view: one full card + back control -->
            <div class="cards">
              <button type="button" class="back-btn" (click)="clearDocSelection.emit(message.id)">
                {{ backLabel }}
              </button>
              <app-repair-case-card [caseSummary]="selectedCase!" />
            </div>
          } @else {
            <!-- Compact list: numbered selectable cards -->
            <div class="cards doc-list">
              @for (c of message.cases; track c.idDocumento; let i = $index) {
                <app-case-summary-card
                  [caseSummary]="c"
                  [badge]="i + 1"
                  (select)="selectDoc.emit({ messageId: message.id, index: i })"
                />
              }
            </div>
          }
        }
      </div>
    </div>
  `,
  styleUrl: './message-bubble.component.css',
})
export class MessageBubbleComponent {
  @Input({ required: true }) message!: ChatMessage;
  @Output() selectCar = new EventEmitter<CarOption>();
  @Output() selectDoc = new EventEmitter<{ messageId: string; index: number }>();
  @Output() clearDocSelection = new EventEmitter<string>();

  get selectedCase(): CaseSummary | null {
    if (this.message.selectedCaseIndex == null || !this.message.cases) return null;
    return this.message.cases[this.message.selectedCaseIndex] ?? null;
  }

  get backLabel(): string {
    const lang = this.message.cases?.[0]?.language ?? 'it';
    return BACK_LABELS[lang] ?? BACK_LABELS['it'];
  }
}
