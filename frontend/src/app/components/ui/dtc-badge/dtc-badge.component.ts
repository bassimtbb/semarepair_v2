import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-dtc-badge',
  standalone: true,
  template: `<span class="dtc-badge">{{ code }}</span>`,
  styleUrl: './dtc-badge.component.css',
})
export class DtcBadgeComponent {
  @Input({ required: true }) code = '';
}
