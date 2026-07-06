import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-kpi-card',
  standalone: true,
  template: `
    <div class="kpi-card bg-surface border-border">
      <div class="kpi-label text-muted">{{ label }}</div>
      <div class="kpi-value text-foreground">{{ value }}</div>
      @if (sublabel) {
        <div class="kpi-sublabel text-muted">{{ sublabel }}</div>
      }
    </div>
  `,
  styleUrl: './kpi-card.component.css',
})
export class KpiCardComponent {
  @Input({ required: true }) label!: string;
  @Input({ required: true }) value!: string;
  @Input() sublabel?: string | null;
}
