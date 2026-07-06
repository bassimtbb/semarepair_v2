import { Component, Input, computed, signal } from '@angular/core';
import { LucideStar } from '@lucide/angular';

// reliability is 1-3 in the data (see services/search/Models/SearchResponse.cs):
// 1 = single report, 2 = confirmed by multiple technicians, 3 = manufacturer-certified.
const LABELS = ['', 'Segnalazione singola', 'Confermato da più tecnici', 'Certificato dal costruttore'];

@Component({
  selector: 'app-star-rating',
  standalone: true,
  imports: [LucideStar],
  // Filled-star color is per-theme, not the plain amber-500 a naive swap
  // would use: measured contrast against the card surface (#f8fafc light /
  // #1e293b dark) found amber-500 fails even the lenient 3:1 UI-component
  // floor in light mode (2.05:1) - amber-700 fixes light (4.80:1) while
  // amber-500 already passes comfortably in dark (6.81:1), so each theme
  // gets its own shade rather than one compromise value.
  // Empty stars stay deliberately low-contrast in light mode (the
  // "unfilled" convention nearly every star-rating UI uses) - the [title]
  // tooltip is the real accessible label, these are a redundant visual cue.
  // Dark mode still gets a real value (slate-500, 3.07:1) rather than
  // reusing the light gray verbatim, which measured at 9.93:1 against the
  // dark surface - backwards, making "empty" visually louder than "filled".
  template: `
    <span class="star-rating" [title]="label()">
      @for (_ of filledArray(); track $index) {
        <svg lucideStar [size]="14" fill="currentColor" class="text-amber-700 dark:text-amber-500"></svg>
      }
      @for (_ of emptyArray(); track $index) {
        <svg lucideStar [size]="14" class="text-gray-300 dark:text-slate-500"></svg>
      }
    </span>
  `,
  styleUrl: './star-rating.component.css',
})
export class StarRatingComponent {
  private readonly reliabilityValue = signal(0);

  @Input({ required: true })
  set reliability(value: number) {
    this.reliabilityValue.set(value);
  }

  private readonly clamped = computed(() => Math.min(3, Math.max(1, this.reliabilityValue())));
  readonly filledArray = computed(() => Array(this.clamped()));
  readonly emptyArray = computed(() => Array(3 - this.clamped()));
  readonly label = computed(() => LABELS[this.clamped()] ?? '');
}
