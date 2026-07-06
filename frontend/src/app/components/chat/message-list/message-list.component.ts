import { AfterViewChecked, Component, ElementRef, EventEmitter, Input, Output, ViewChild } from '@angular/core';
import { MessageBubbleComponent } from '../message-bubble/message-bubble.component';
import { TypingIndicatorComponent } from '../../ui/typing-indicator/typing-indicator.component';
import type { CarOption, ChatMessage } from '../../../models/chat.models';

@Component({
  selector: 'app-message-list',
  standalone: true,
  imports: [MessageBubbleComponent, TypingIndicatorComponent],
  template: `
    <div class="message-list bg-background" #scrollContainer>
      @for (message of messages; track message.id) {
        <app-message-bubble [message]="message" (selectCar)="selectCar.emit($event)" />
      }
      @if (isStreaming) {
        <app-typing-indicator />
      }
    </div>
  `,
  styleUrl: './message-list.component.css',
})
export class MessageListComponent implements AfterViewChecked {
  @Input({ required: true }) messages: ChatMessage[] = [];
  @Input() isStreaming = false;
  @Output() selectCar = new EventEmitter<CarOption>();

  @ViewChild('scrollContainer') private scrollContainer?: ElementRef<HTMLDivElement>;

  private lastMessageCount = 0;

  ngAfterViewChecked(): void {
    if (this.messages.length !== this.lastMessageCount) {
      this.lastMessageCount = this.messages.length;
      const el = this.scrollContainer?.nativeElement;
      if (el) el.scrollTop = el.scrollHeight;
    }
  }
}
