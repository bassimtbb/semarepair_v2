import { Component, computed } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { LucideX, LucideSun, LucideMoon, LucideMessageCircle, LucideCircleHelp } from '@lucide/angular';
import { ChatStore } from './services/chat-store.service';
import { ThemeService } from './services/theme.service';
import { HelpPanelService } from './services/help-panel.service';
import { UiLanguageService } from './services/ui-language.service';
import { h } from './services/help-strings';
import type { CarOption } from './models/chat.models';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterLink, RouterOutlet, LucideX, LucideSun, LucideMoon, LucideMessageCircle, LucideCircleHelp],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css',
})
export class AppComponent {
  constructor(
    readonly chat: ChatStore,
    readonly theme: ThemeService,
    readonly help: HelpPanelService,
    private readonly ui: UiLanguageService,
  ) {}

  // Only the "?" tooltip needs translating up here; the drawer carries the
  // rest of its own text.
  readonly helpStrings = computed(() => h(this.ui.lang()));

  // Secondary line of the centered confirmed-car badge - everything
  // CarOption carries beyond marca+modello (shown on the primary line).
  // annoFine 9999 means "still in production" (same convention as
  // RepairOrchestrator.BuildVehicleNotFoundMessage's "dal X a oggi") -
  // shown as "oggi" here too, not the literal placeholder year.
  carDetails(car: CarOption): string {
    const parts: string[] = [];

    if (car.motorizzazione) parts.push(car.motorizzazione);

    if (car.annoInizio) {
      const to = car.annoFine === 9999 ? 'oggi' : car.annoFine ?? '';
      parts.push(`${car.annoInizio}–${to}`);
    }

    if (car.alimentazione) parts.push(car.alimentazione);

    const power = `${car.kw ? car.kw + ' kW' : ''}${car.kw && car.cavalli ? ' / ' : ''}${car.cavalli ? car.cavalli + ' CV' : ''}`;
    if (power) parts.push(power);

    if (car.codiceMotore) parts.push(car.codiceMotore);

    return parts.join(' · ');
  }
}
