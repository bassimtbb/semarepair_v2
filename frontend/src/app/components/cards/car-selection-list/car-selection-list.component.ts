import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { LucideChevronRight } from '@lucide/angular';
import { CarCardComponent } from '../car-card/car-card.component';
import type { CarOption } from '../../../models/chat.models';
import { sortCarsForDisplay } from '../../../utils/car-sort';

interface NumberedCar {
  car: CarOption;
  badge: number;
}

interface CarGroup {
  key: string;
  cars: NumberedCar[];
}

// Groups a car-selection result by motorizzazione (the engine label a
// mechanic actually recognizes, e.g. "1.5 TDCi 8v" - see VehicleSearchService's
// own motorizzazione/codiceMotore distinction) rather than by brand/model,
// since most real results already share one brand+model and only differ
// by engine variant/year/power - exactly the case that's hard to scan as a
// flat grid once there are dozens of near-identical cards. Cars with no
// motorizzazione at all (rare, but FindCar/symptom results can omit it)
// fall into a literal "Altro" group, sorted last.
@Component({
  selector: 'app-car-selection-list',
  standalone: true,
  imports: [CarCardComponent, LucideChevronRight],
  template: `
    <div class="car-selection">
      @for (group of groups(); track group.key) {
        <div class="car-group">
          <button
            type="button"
            class="group-header text-foreground hover:bg-foreground/8"
            [class.expanded]="isExpanded(group.key)"
            (click)="toggleGroup(group.key)"
          >
            <svg lucideChevronRight [size]="14"></svg>
            <span class="group-label">{{ group.key }}</span>
            <span class="group-count text-muted">({{ group.cars.length }})</span>
          </button>
          @if (isExpanded(group.key)) {
            <div class="car-grid">
              @for (item of group.cars; track item.car.idMacchina) {
                <app-car-card [car]="item.car" [badge]="item.badge" (select)="select.emit($event)" />
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styleUrl: './car-selection-list.component.css',
})
export class CarSelectionListComponent {
  @Input({ required: true }) cars!: CarOption[];
  @Output() select = new EventEmitter<CarOption>();

  // Collapsed-by-key, not expanded-by-key, so that newly arriving groups
  // (a fresh car-selection result replacing the previous one) default to
  // expanded without needing to be seeded - only explicit user action ever
  // adds to this set.
  private readonly collapsedKeys = signal<ReadonlySet<string>>(new Set());

  readonly groups = computed<CarGroup[]>(() => {
    // sortCarsForDisplay produces the exact flat visual order that ChatStore's
    // carDisplayOrder also uses — badge numbers here are therefore always
    // identical to the indices the selection parsers resolve to.
    const flat = sortCarsForDisplay(this.cars);
    const byKey = new Map<string, NumberedCar[]>();
    flat.forEach((car, i) => {
      const key = car.motorizzazione?.trim() || 'Altro';
      const list = byKey.get(key) ?? [];
      list.push({ car, badge: i + 1 });
      byKey.set(key, list);
    });
    // Map preserves insertion order, which mirrors flat's group order (alpha, Altro last).
    return [...byKey.entries()].map(([key, cars]) => ({ key, cars }));
  });

  isExpanded(key: string): boolean {
    return !this.collapsedKeys().has(key);
  }

  toggleGroup(key: string): void {
    const next = new Set(this.collapsedKeys());
    if (next.has(key)) {
      next.delete(key);
    } else {
      next.add(key);
    }
    this.collapsedKeys.set(next);
  }
}
