import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CarSelectionListComponent } from '../../cards/car-selection-list/car-selection-list.component';
import { RepairCaseCardComponent } from '../../cards/repair-case-card/repair-case-card.component';
import type { CarOption, ChatMessage } from '../../../models/chat.models';

@Component({
  selector: 'app-message-bubble',
  standalone: true,
  imports: [CarSelectionListComponent, RepairCaseCardComponent],
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
          <div class="cards">
            @for (c of message.cases; track c.idDocumento) {
              <app-repair-case-card [caseSummary]="c" />
            }
          </div>
        }
      </div>
    </div>
  `,
  styleUrl: './message-bubble.component.css',
})
export class MessageBubbleComponent {
  @Input({ required: true }) message!: ChatMessage;
  @Output() selectCar = new EventEmitter<CarOption>();
}
