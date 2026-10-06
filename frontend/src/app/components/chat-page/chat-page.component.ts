import { Component, ViewChild } from '@angular/core';
import { MessageListComponent } from '../chat/message-list/message-list.component';
import { ChatInputComponent } from '../chat/chat-input/chat-input.component';
import { HelpDrawerComponent } from '../help-drawer/help-drawer.component';
import { ChatStore } from '../../services/chat-store.service';

// Extracted out of AppComponent when routing was introduced (the usage
// dashboard's route - see app.routes.ts) - the chat shell itself
// (message list + input bar) is now ITS OWN routed page, while the
// header/theme-toggle/confirmed-car badge in AppComponent stay global
// across both routes.
//
// The help drawer lives here rather than in AppComponent for one reason:
// its suggestions have to reach the input bar, and both are on this page.
// The "?" that opens it is in the global header and drives it through
// HelpPanelService, so neither component has to know about the other.
@Component({
  selector: 'app-chat-page',
  standalone: true,
  imports: [MessageListComponent, ChatInputComponent, HelpDrawerComponent],
  template: `
    <app-message-list
      [messages]="chat.messages()"
      [isStreaming]="chat.isStreaming()"
      (selectCar)="chat.confirmCar($event)"
      (selectDoc)="handleSelectDoc($event)"
      (clearDocSelection)="chat.clearDocumentSelection($event)"
    />
    <app-chat-input #composer [disabled]="chat.isStreaming()" (send)="chat.sendMessage($event)" />
    <app-help-drawer (suggest)="handleSuggestion($event)" />
  `,
  styleUrl: './chat-page.component.css',
})
export class ChatPageComponent {
  @ViewChild('composer') composer?: ChatInputComponent;

  constructor(readonly chat: ChatStore) {}

  // Every suggestion lands in the box UNSENT, and none of them picks a
  // vehicle. That is the product's flow and it is not an implementation
  // detail: the mechanic says the symptom or the code, the system answers
  // with the vehicles that documentation covers, and HE chooses which one
  // he is working on. A chip that confirmed a car on his behalf skipped the
  // step the whole interface is built around - it was briefly tried here,
  // and it was wrong.
  handleSuggestion(text: string): void {
    this.composer?.setText(text);
  }

  handleSelectDoc(e: { messageId: string; index: number }): void {
    this.chat.selectDocument(e.messageId, e.index);
  }
}
