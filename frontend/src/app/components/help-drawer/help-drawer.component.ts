import { Component, EventEmitter, Output, computed } from '@angular/core';
import { HelpPanelService } from '../../services/help-panel.service';
import { UiLanguageService } from '../../services/ui-language.service';
import { h } from '../../services/help-strings';

@Component({
  selector: 'app-help-drawer',
  standalone: true,
  template: `
    @if (help.isOpen()) {
      <div class="help-backdrop" (click)="help.close()" (keydown.escape)="help.close()" role="presentation">
        <section class="help-drawer bg-surface border-border text-foreground" role="dialog" aria-modal="true" [attr.aria-label]="strings().title" (click)="$event.stopPropagation()">
          <header><div><h2>{{ strings().heading }}</h2><p>{{ strings().intro }}</p></div><button type="button" (click)="help.close()" [attr.aria-label]="strings().close">×</button></header>
          <div class="language-row"><label for="ui-language">{{ strings().language }}</label><select id="ui-language" [value]="ui.lang()" (change)="ui.setLanguage($any($event.target).value)"><option value="it">Italiano</option><option value="en">English</option><option value="fr">Français</option><option value="pt">Português</option><option value="es">Español</option></select></div>
          <div class="suggestions">@for (suggestion of strings().suggestions; track suggestion) {<button type="button" (click)="suggest.emit(suggestion); help.close()">{{ suggestion }} <span aria-hidden="true">→</span></button>}</div>
        </section>
      </div>
    }
  `,
  styleUrl: './help-drawer.component.css',
})
export class HelpDrawerComponent {
  @Output() readonly suggest = new EventEmitter<string>();
  readonly strings = computed(() => h(this.ui.lang()));

  constructor(readonly help: HelpPanelService, readonly ui: UiLanguageService) {}
}
