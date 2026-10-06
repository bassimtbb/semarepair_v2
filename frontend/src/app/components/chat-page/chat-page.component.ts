import { Component, ViewChild } from '@angular/core';
import { MessageListComponent } from '../chat/message-list/message-list.component';
import { ChatInputComponent } from '../chat/chat-input/chat-input.component';
import { HelpDrawerComponent } from '../help-drawer/help-drawer.component';
import type { Suggestion } from '../help-drawer/help-drawer.component';
import { CoverageService } from '../../services/coverage.service';
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

  constructor(readonly chat: ChatStore, private readonly coverage: CoverageService) {}

  // A suggestion with no vehicle goes into the box unsent: the reader sees
  // the sentence appear where his own will go, and can edit it first.
  //
  // One that carries a vehicle is SENT, because it cannot work otherwise -
  // it confirms that car and asks in the same turn. A technical question
  // with no car confirmed can only come back as "which vehicle?" (Rule 1),
  // so placing it in the box would hand the mechanic a dead end.
  handleSuggestion(s: Suggestion): void {
    if (s.car) void this.chat.askFor(this.coverage.asCarOption(s.car), s.text);
    else this.composer?.setText(s.text);
  }

  handleSelectDoc(e: { messageId: string; index: number }): void {
    this.chat.selectDocument(e.messageId, e.index);
  }
}
