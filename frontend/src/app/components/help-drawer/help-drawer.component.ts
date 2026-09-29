import { Component, EventEmitter, HostListener, OnInit, Output, computed } from '@angular/core';
import {
  LucideX, LucideStar, LucideMessageCircle, LucideChevronRight,
  LucideSparkles, LucideAudioLines, LucideMaximize, LucideShieldCheck,
  LucideLanguages,
} from '@lucide/angular';
import { HelpPanelService } from '../../services/help-panel.service';
import { UiLanguageService } from '../../services/ui-language.service';
import { CoverageService } from '../../services/coverage.service';
import { ChatStore } from '../../services/chat-store.service';
import { h, LANGUAGE_NAMES } from '../../services/help-strings';
import type { HelpLanguage } from '../../services/help-strings';

// The help drawer: that this is a demo carrying one vehicle, how to drive
// it, and four questions known to return something.
//
// It slides in over the chat with NO backdrop, and a click outside does
// not close it. Both are deliberate, and both were wrong in the first
// version: it had a full-screen backdrop bound to close(), so reaching for
// the input bar to try what you had just read dismissed the panel. The
// point of a drawer rather than a dialog is that the reader can test while
// he reads. Escape and the close button are the ways out.
@Component({
  selector: 'app-help-drawer',
  standalone: true,
  imports: [
    LucideX, LucideStar, LucideMessageCircle, LucideChevronRight,
    LucideSparkles, LucideAudioLines, LucideMaximize, LucideShieldCheck,
    LucideLanguages,
  ],
  template: `
    <aside
      class="help-drawer bg-background border-border"
      [class.help-drawer--open]="help.isOpen()"
      [attr.aria-hidden]="!help.isOpen()"
      role="complementary"
      [attr.aria-label]="t().title"
    >
      <header class="help-head border-border">
        <span class="help-title text-foreground">{{ t().title }}</span>
        <span class="help-badge text-accent">{{ t().demo_badge }}</span>
        <button
          type="button"
          class="help-close text-muted hover:bg-foreground/8"
          (click)="help.close()"
          [title]="t().close"
          [attr.aria-label]="t().close"
        >
          <svg lucideX [size]="16"></svg>
        </button>
      </header>

      <div class="help-body">

        <!-- La mention demo, en premier. Un client a qui on envoie un lien
             doit savoir que l'archive ne porte qu'un seul vehicule AVANT
             de tester : annonce, c'est un perimetre assume ; passee sous
             silence, sa premiere question sur une autre voiture se lit
             comme un produit en panne. -->
        <section class="help-section help-section--demo bg-surface border-border">
          <div class="help-section-head text-accent">
            <svg lucideStar [size]="15"></svg>
            <span>{{ t().demo_title }}</span>
          </div>
          <p class="help-text text-foreground">{{ t().demo_body }}</p>

          @if (vehicleLabel(); as v) {
            <p class="help-counts text-muted">{{ t().demo_vehicle(v) }}</p>
          }
          @if (countsLine(); as counts) {
            <p class="help-counts text-muted">{{ counts }}</p>
          }
          @if (repairLine(); as repairs) {
            <p class="help-counts text-muted">{{ repairs }}</p>
          }
        </section>

        <section class="help-section">
          <div class="help-section-head text-accent">
            <svg lucideMessageCircle [size]="15"></svg>
            <span>{{ t().howto_title }}</span>
          </div>
          <ol class="help-steps text-foreground">
            <li>{{ t().howto_1 }}</li>
            <li>{{ t().howto_2 }}</li>
            <li>{{ t().howto_3 }}</li>
          </ol>
        </section>

        <!-- Un clic ecrit la phrase dans le champ SANS l'envoyer, et le
             tiroir reste ouvert : le lecteur voit le texte arriver la ou
             il devra taper le sien, et peut le modifier avant de valider. -->
        <section class="help-section">
          <div class="help-section-head text-accent">
            <svg lucideChevronRight [size]="15"></svg>
            <span>{{ t().try_title }}</span>
          </div>
          <div class="help-chips">
            @for (q of suggestions(); track q) {
              <button
                type="button"
                class="help-chip bg-surface border-border text-foreground hover:bg-foreground/8"
                (click)="suggest.emit(q)"
              >{{ q }}</button>
            }
          </div>
          <p class="help-hint text-muted">{{ t().try_hint }}</p>
        </section>

        <section class="help-section">
          <div class="help-section-head text-accent">
            <svg lucideSparkles [size]="15"></svg>
            <span>{{ t().voice_title }}</span>
          </div>
          <p class="help-text text-foreground">{{ t().voice_body }}</p>
          <ul class="help-legend text-muted">
            <li><svg lucideSparkles [size]="13"></svg><span>{{ t().voice_hd }}</span></li>
            <li><svg lucideAudioLines [size]="13"></svg><span>{{ t().voice_std }}</span></li>
          </ul>
        </section>

        <section class="help-section">
          <div class="help-section-head text-accent">
            <svg lucideMaximize [size]="15"></svg>
            <span>{{ t().zoom_title }}</span>
          </div>
          <p class="help-text text-foreground">{{ t().zoom_body }}</p>
        </section>

        <!-- Ce n'est pas une excuse : pour un atelier, un systeme qui
             invente une procedure de freinage est un danger, et un systeme
             qui dit "je n'ai pas" est un outil. -->
        <section class="help-section">
          <div class="help-section-head text-accent">
            <svg lucideShieldCheck [size]="15"></svg>
            <span>{{ t().limits_title }}</span>
          </div>
          <p class="help-text text-foreground">{{ t().limits_body }}</p>
        </section>

        <!-- Chaque option porte ce que l'archive couvre reellement dans
             cette langue. Proposer une langue vide serait une promesse que
             les donnees ne peuvent pas tenir. -->
        <section class="help-section">
          <div class="help-section-head text-accent">
            <svg lucideLanguages [size]="15"></svg>
            <span>{{ t().language_title }}</span>
          </div>
          <select
            class="help-select bg-surface border-border text-foreground"
            [value]="ui.lang()"
            (change)="ui.setLanguage($any($event.target).value)"
            [attr.aria-label]="t().language_title"
          >
            @for (code of languages; track code) {
              <option [value]="code">{{ languageName(code) }}{{ coverageSuffix(code) }}</option>
            }
          </select>
        </section>

      </div>
    </aside>
  `,
  styleUrl: './help-drawer.component.css',
})
export class HelpDrawerComponent implements OnInit {
  // The chosen question, for the page to write into the input bar. The
  // drawer does not reach into the composer itself - it says what was
  // picked and lets the page place it.
  @Output() readonly suggest = new EventEmitter<string>();

  readonly languages = Object.keys(LANGUAGE_NAMES) as HelpLanguage[];

  readonly t = computed(() => h(this.ui.lang()));

  constructor(
    readonly help: HelpPanelService,
    readonly ui: UiLanguageService,
    private readonly coverageService: CoverageService,
    private readonly chat: ChatStore,
  ) {}

  ngOnInit(): void {
    this.coverageService.load();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.help.isOpen()) this.help.close();
  }

  readonly vehicleLabel = computed(() => {
    const first = this.coverageService.coverage()?.vehicles[0];
    return first ? this.coverageService.vehicleLabel(first) : null;
  });

  readonly countsLine = computed(() => {
    const c = this.coverageService.forLanguage(this.ui.lang());
    if (!c) return null;

    const s = this.t();
    if (c.manualPages > 0 && c.diagrams > 0) return s.demo_counts(c.facts, c.diagrams, c.manualPages);
    if (c.diagrams > 0) return s.demo_counts_no_manual(c.facts, c.diagrams);
    return s.demo_counts_data_only(c.facts);
  });

  readonly repairLine = computed(() => {
    const n = this.coverageService.coverage()?.repairDocuments;
    return n ? this.t().demo_repairs(n) : null;
  });

  // Contextual, and filtered by what the selected language actually holds.
  //
  // Before a vehicle is confirmed the only offer is the vehicle itself:
  // asking about a fuse first earns a clarifying question, and a first
  // impression of not having been understood. After confirmation, the two
  // diagram questions appear only where diagrams exist - in French,
  // Spanish and Portuguese the archive has none, and offering them there
  // would be the drawer breaking the one rule the product keeps.
  readonly suggestions = computed<string[]>(() => {
    const s = this.t();

    if (!this.chat.confirmedCar()) {
      const first = this.coverageService.coverage()?.vehicles[0];
      return first ? [this.coverageService.vehicleQuery(first)] : [];
    }

    const out = [s.q_fuse, s.q_fusebox];
    const c = this.coverageService.forLanguage(this.ui.lang());
    if (c && c.diagrams > 0) out.push(s.q_diagram, s.q_airbag);
    return out;
  });

  languageName(code: HelpLanguage): string {
    return LANGUAGE_NAMES[code];
  }

  coverageSuffix(code: HelpLanguage): string {
    const c = this.coverageService.forLanguage(code);
    if (!c) return '';

    const s = this.t();
    if (c.manualPages > 0 && c.diagrams > 0) return ` — ${s.coverage_full}`;
    if (c.diagrams > 0) return ` — ${s.coverage_no_manual}`;
    return ` — ${s.coverage_data_only}`;
  }
}
