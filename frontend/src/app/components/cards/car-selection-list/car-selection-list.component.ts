import { Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideChevronRight, LucideSearch } from '@lucide/angular';
import { CarCardComponent } from '../car-card/car-card.component';
import type { CarOption } from '../../../models/chat.models';

interface CarGroup {
  key: string;
  cars: CarOption[];
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
  imports: [FormsModule, CarCardComponent, LucideChevronRight, LucideSearch],
  template: `
    <div class="car-selection">
      <div class="filter-bar bg-card-surface border-border">
        <svg lucideSearch [size]="14" class="text-muted"></svg>
        <input
          type="text"
          class="text-foreground"
          [(ngModel)]="filterText"
          placeholder="Filtra per motore, anno, codice..."
        />
      </div>

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
              @for (car of group.cars; track car.idMacchina) {
                <app-car-card [car]="car" (select)="select.emit($event)" />
              }
            </div>
          }
        </div>
      }

      @if (groups().length === 0) {
        <div class="no-matches text-muted">Nessun veicolo corrisponde al filtro.</div>
      }
    </div>
  `,
  styleUrl: './car-selection-list.component.css',
})
export class CarSelectionListComponent {
  @Input({ required: true }) cars!: CarOption[];
  @Output() select = new EventEmitter<CarOption>();

  private readonly filter = signal('');
  // Collapsed-by-key, not expanded-by-key, so that newly arriving groups
  // (a fresh car-selection result replacing the previous one) default to
  // expanded without needing to be seeded - only explicit user action ever
  // adds to this set.
  private readonly collapsedKeys = signal<ReadonlySet<string>>(new Set());

  get filterText(): string {
    return this.filter();
  }

  set filterText(value: string) {
    this.filter.set(value);
  }

  readonly groups = computed<CarGroup[]>(() => {
    const term = this.filter().trim().toLowerCase();
    const filtered = term
      ? this.cars.filter((car) => this.searchableText(car).includes(term))
      : this.cars;

    const byKey = new Map<string, CarOption[]>();
    for (const car of filtered) {
      const key = car.motorizzazione?.trim() || 'Altro';
      const list = byKey.get(key) ?? [];
      list.push(car);
      byKey.set(key, list);
    }

    return [...byKey.entries()]
      .sort(([a], [b]) => (a === 'Altro' ? 1 : b === 'Altro' ? -1 : a.localeCompare(b)))
      .map(([key, cars]) => ({
        key,
        cars: [...cars].sort(
          (a, b) => (b.annoInizio ?? 0) - (a.annoInizio ?? 0) || a.codiceMotore.localeCompare(b.codiceMotore),
        ),
      }));
  });

  // While filtering, every matching group is shown fully open - the
  // mechanic is actively searching for something specific and shouldn't
  // also have to manually expand each group to see whether it matched.
  isExpanded(key: string): boolean {
    return this.filter().trim().length > 0 || !this.collapsedKeys().has(key);
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

  private searchableText(car: CarOption): string {
    return [car.marca, car.modello, car.motorizzazione, car.codiceMotore, car.alimentazione, car.annoInizio, car.annoFine]
      .filter((v) => v !== null && v !== undefined && v !== '')
      .join(' ')
      .toLowerCase();
  }
}
