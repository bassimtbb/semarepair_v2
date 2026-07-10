import type { CarOption } from '../models/chat.models';

// Returns the flat list of cars in the visual display order used by
// CarSelectionListComponent: groups sorted alphabetically (Altro last),
// within each group by annoInizio desc then codiceMotore asc.
//
// Both ChatStore.carDisplayOrder and CarSelectionListComponent call this
// function so the badge numbers on cards always match the indices the
// selection parsers use — single source of truth for visual ordering.
export function sortCarsForDisplay(cars: CarOption[]): CarOption[] {
  const byKey = new Map<string, CarOption[]>();
  for (const car of cars) {
    const key = car.motorizzazione?.trim() || 'Altro';
    const list = byKey.get(key) ?? [];
    list.push(car);
    byKey.set(key, list);
  }
  return [...byKey.entries()]
    .sort(([a], [b]) => (a === 'Altro' ? 1 : b === 'Altro' ? -1 : a.localeCompare(b)))
    .flatMap(([, groupCars]) =>
      [...groupCars].sort(
        (a, b) =>
          (b.annoInizio ?? 0) - (a.annoInizio ?? 0) ||
          a.codiceMotore.localeCompare(b.codiceMotore),
      ),
    );
}
