import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CarSelectionListComponent } from '../../cards/car-selection-list/car-selection-list.component';
import { RepairCaseCardComponent } from '../../cards/repair-case-card/repair-case-card.component';
import { CaseSummaryCardComponent } from '../../cards/case-summary-card/case-summary-card.component';
import { TechnicalAnswerCardComponent } from '../../cards/technical-answer-card/technical-answer-card.component';
import { renderInlineMarkdown } from '../../../utils/markdown';
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
  imports: [CarSelectionListComponent, RepairCaseCardComponent, CaseSummaryCardComponent, TechnicalAnswerCardComponent],
  template: `
    <div class="bubble-row" [class.user]="message.role === 'user'">
      <div class="bubble bg-bubble border-border text-foreground" [class.user]="message.role === 'user'">
        @if (message.text) {
          <div class="text" [innerHTML]="renderedText"></div>
        }

        @if (message.technicalChunks && message.technicalChunks.length > 0) {
          <div class="cards tech-list">
            @for (chunk of message.technicalChunks; track $index) {
              <app-technical-answer-card [chunk]="chunk" />
            }
          </div>
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

  // Memoized on the source text: a getter that rebuilt the string on every
  // call would hand [innerHTML] a fresh reference each change-detection
  // cycle, re-running Angular's sanitizer and rewriting the DOM node every
  // tick. Streaming replaces message.text wholesale (one SSE event = one
  // full ChatResponse), so a single-entry cache is enough.
  private renderedSource: string | null = null;
  private renderedHtml = '';

  get renderedText(): string {
    const src = this.message.text ?? '';
    if (src !== this.renderedSource) {
      this.renderedSource = src;
      this.renderedHtml = renderInlineMarkdown(src);
    }
    return this.renderedHtml;
  }

  get selectedCase(): CaseSummary | null {
    if (this.message.selectedCaseIndex == null || !this.message.cases) return null;
    return this.message.cases[this.message.selectedCaseIndex] ?? null;
  }

  get backLabel(): string {
    const lang = this.message.cases?.[0]?.language ?? 'it';
    return BACK_LABELS[lang] ?? BACK_LABELS['it'];
  }
}
