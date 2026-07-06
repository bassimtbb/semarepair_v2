import { Component } from '@angular/core';

@Component({
  selector: 'app-typing-indicator',
  standalone: true,
  template: `
    <div class="typing">
      <span></span><span></span><span></span>
    </div>
  `,
  styleUrl: './typing-indicator.component.css',
})
export class TypingIndicatorComponent {}
