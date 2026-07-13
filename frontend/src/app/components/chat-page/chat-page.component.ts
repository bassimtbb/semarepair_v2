import { Component } from '@angular/core';
import { MessageListComponent } from '../chat/message-list/message-list.component';
import { ChatInputComponent } from '../chat/chat-input/chat-input.component';
import { ChatStore } from '../../services/chat-store.service';

// Extracted out of AppComponent when routing was introduced (the usage
// dashboard's route - see app.routes.ts) - the chat shell itself
// (message list + input bar) is now ITS OWN routed page, while the
// header/theme-toggle/confirmed-car badge in AppComponent stay global
// across both routes.
@Component({
  selector: 'app-chat-page',
  standalone: true,
  imports: [MessageListComponent, ChatInputComponent],
  template: `
    <app-message-list
      [messages]="chat.messages()"
      [isStreaming]="chat.isStreaming()"
      (selectCar)="chat.confirmCar($event)"
      (selectDoc)="handleSelectDoc($event)"
      (clearDocSelection)="chat.clearDocumentSelection($event)"
    />
    <app-chat-input [disabled]="chat.isStreaming()" (send)="chat.sendMessage($event)" />
  `,
  styleUrl: './chat-page.component.css',
})
export class ChatPageComponent {
  constructor(readonly chat: ChatStore) {}

  handleSelectDoc(e: { messageId: string; index: number }): void {
    this.chat.selectDocument(e.messageId, e.index);
  }
}
