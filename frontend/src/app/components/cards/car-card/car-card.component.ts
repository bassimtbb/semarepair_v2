import { Component, EventEmitter, Input, Output } from '@angular/core';
import type { CarOption } from '../../../models/chat.models';

@Component({
  selector: 'app-car-card',
  standalone: true,
  template: `
    <button
      type="button"
      class="car-card bg-card-surface border-border hover:border-accent hover:bg-foreground/8"
      (click)="select.emit(car)"
    >
      @if (badge > 0) {
        <span class="car-badge">{{ badge }}</span>
      }
      <div class="car-title text-accent">{{ car.marca }} {{ car.modello }}</div>
      <div class="car-detail text-muted">{{ car.motorizzazione }}</div>
      <div class="car-meta text-muted">
        <span class="engine-code">{{ car.codiceMotore }}</span>
        @if (car.annoInizio) {
          <span class="years">{{ car.annoInizio }}–{{ car.annoFine === 9999 ? 'oggi' : (car.annoFine ?? '') }}</span>
        }
        @if (car.kw || car.cavalli) {
          <span class="power">
            {{ car.kw ? car.kw + ' kW' : '' }}{{ car.kw && car.cavalli ? ' / ' : '' }}{{ car.cavalli ? car.cavalli + ' CV' : '' }}
          </span>
        }
      </div>
    </button>
  `,
  styleUrl: './car-card.component.css',
})
export class CarCardComponent {
  @Input({ required: true }) car!: CarOption;
  @Input() badge = 0;
  @Output() select = new EventEmitter<CarOption>();
}
