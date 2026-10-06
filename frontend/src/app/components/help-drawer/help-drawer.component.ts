import { Component, EventEmitter, HostListener, OnInit, Output, computed, effect } from '@angular/core';
import {
  LucideX, LucideStar, LucideMessageCircle, LucideCar, LucideWrench, LucideImage,
  LucideHash, LucideZap, LucideBookOpen, LucideGauge, LucideSparkles,
  LucideAudioLines, LucideMaximize, LucideShieldCheck, LucideLanguages,
} from '@lucide/angular';
import { HelpPanelService } from '../../services/help-panel.service';
import { UiLanguageService } from '../../services/ui-language.service';
import { CoverageService } from '../../services/coverage.service';
import type { CoverageSection, CoverageVehicle } from '../../services/coverage.service';
import { ChatStore } from '../../services/chat-store.service';
import { h, LANGUAGE_NAMES } from '../../services/help-strings';
import type { HelpLanguage } from '../../services/help-strings';

// The help drawer: what this demo holds, vehicle by vehicle, and questions
// that are known to return something.
//
// It slides in over the chat with NO backdrop and a click outside does not
// close it - .app-shell pads itself by the drawer's width instead, so the
// chat narrows rather than being covered. Both were wrong in the first
// version: it was a dialog sitting on the input bar the reader was invited
// to type into. Escape and the close button are the ways out.
//
// Everything below the headings is read from /api/search/coverage. The
// archive went from one vehicle to four in an afternoon and not a line here
// changed - which is the whole point, because a panel that states counts
// and sample questions is making promises, and a promise compiled into the
// frontend goes stale on the next delivery.
@Component({
  selector: 'app-help-drawer',
  standalone: true,
  imports: [
    LucideX, LucideStar, LucideMessageCircle, LucideCar, LucideWrench, LucideImage,
    LucideHash, LucideZap, LucideBookOpen, LucideGauge, LucideSparkles,
    LucideAudioLines, LucideMaximize, LucideShieldCheck, LucideLanguages,
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

        <!-- La mention demo, en premier et encadree. Un client a qui on
             envoie un lien doit savoir AVANT de tester que l'archive est
             volontairement reduite - et, depuis qu'il y a quatre voitures,
             qu'elles ne portent pas toutes la meme chose. -->
        <section class="help-section help-section--demo bg-surface border-border">
          <div class="help-section-head text-accent">
            <svg lucideStar [size]="15"></svg>
            <span>{{ t().demo_title }}</span>
          </div>
          <p class="help-text text-foreground">{{ t().demo_body(coverage.vehicles().length) }}</p>
        </section>

        @if (selected(); as car) {
          <!-- Un vehicule est confirme : on ne montre plus que le sien. -->
          <section class="help-section">
            <div class="help-section-head text-accent">
              <svg lucideCar [size]="15"></svg>
              <span>{{ car.marca }} {{ car.modello }}</span>
            </div>
            <p class="help-counts text-muted">{{ coverage.label(car) }}</p>
          </section>

          <!-- Les sections, dans l'ordre de ce que le produit SERT a faire :
               trouver la panne d'abord, illustrer ensuite. Une section que
               ce vehicule ne peut pas remplir n'est pas rendue du tout -
               la BMW n'a aucun code guasto, et ne rien dire le dit mieux
               qu'une liste vide sous un titre qui en promet. -->
          @for (s of sections(car); track s.key) {
            @if (s.section.total > 0 && s.section.examples.length) {
              <section class="help-section">
                <div class="help-section-head text-accent">
                  @switch (s.key) {
                    @case ('cases')     { <svg lucideWrench [size]="15"></svg> }
                    @case ('photos')    { <svg lucideImage [size]="15"></svg> }
                    @case ('codes')     { <svg lucideHash [size]="15"></svg> }
                    @case ('diagrams')  { <svg lucideZap [size]="15"></svg> }
                    @case ('manual')    { <svg lucideBookOpen [size]="15"></svg> }
                    @case ('technical') { <svg lucideGauge [size]="15"></svg> }
                  }
                  <span>{{ s.title }}</span>
                  <span class="help-codes-count text-muted">{{ s.section.total }} {{ s.unit }}</span>
                </div>

                @if (s.key === 'codes') {
                  @for (group of coverage.groupFaultCodes(s.section.examples); track group.prefix) {
                    <div class="help-codes-group">
                      <div class="help-codes-label text-muted">{{ groupLabel(group.prefix) }}</div>
                      <div class="help-codes-grid">
                        @for (code of group.codes; track code) {
                          <button type="button"
                                  class="help-code bg-surface border-border text-foreground hover:bg-foreground/8"
                                  (click)="suggest.emit(code)">{{ code }}</button>
                        }
                      </div>
                    </div>
                  }
                  <p class="help-hint text-muted">{{ t().codes_hint }}</p>
                } @else {
                  <div class="help-chips">
                    @for (q of s.section.examples; track q) {
                      <button type="button"
                              class="help-chip bg-surface border-border text-foreground hover:bg-foreground/8"
                              (click)="suggest.emit(q)">{{ q }}</button>
                    }
                  </div>
                  <p class="help-hint text-muted">{{ t().try_hint }}</p>
                }
              </section>
            }
          }
        } @else if (coverage.vehicles().length) {
          <!-- Aucun vehicule confirme : le choix du vehicule EST la premiere
               question. Poser une question technique avant lui ne peut que
               retourner une demande de clarification. -->
          <section class="help-section">
            <div class="help-section-head text-accent">
              <svg lucideCar [size]="15"></svg>
              <span>{{ t().choose_vehicle }}</span>
            </div>
            <div class="help-chips">
              @for (v of coverage.vehicles(); track v.idMacchina) {
                <button type="button"
                        class="help-vehicle bg-surface border-border text-foreground hover:bg-foreground/8"
                        (click)="suggest.emit(v.query)">
                  <span class="help-vehicle-name">{{ v.marca }} {{ v.modello }}</span>
                  <span class="help-vehicle-spec text-muted">{{ coverage.label(v) }}</span>
                  <span class="help-vehicle-holds text-muted">{{ holdings(v) }}</span>
                </button>
              }
            </div>
            <p class="help-hint text-muted">{{ t().choose_vehicle_hint }}</p>
          </section>
        }

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

        <!-- Pour un atelier, un systeme qui invente une procedure de freinage
             est un danger ; un systeme qui dit "je n'ai pas" est un outil. -->
        <section class="help-section">
          <div class="help-section-head text-accent">
            <svg lucideShieldCheck [size]="15"></svg>
            <span>{{ t().limits_title }}</span>
          </div>
          <p class="help-text text-foreground">{{ t().limits_body }}</p>
        </section>

        <!-- Chaque option porte ce que l'archive couvre reellement dans
             cette langue : proposer une langue vide serait une promesse que
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
  // drawer does not reach into the composer - it says what was picked and
  // lets the page place it.
  @Output() readonly suggest = new EventEmitter<string>();

  readonly languages = Object.keys(LANGUAGE_NAMES) as HelpLanguage[];
  readonly t = computed(() => h(this.ui.lang()));

  constructor(
    readonly help: HelpPanelService,
    readonly ui: UiLanguageService,
    readonly coverage: CoverageService,
    private readonly chat: ChatStore,
  ) {
    // The suggestions are the archive's own words, so they are in the
    // archive's language - switching the menu has to refetch them, not just
    // relabel the headings around them.
    effect(() => this.coverage.load(this.ui.lang()));
  }

  ngOnInit(): void {
    this.coverage.load(this.ui.lang());
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.help.isOpen()) this.help.close();
  }

  // The confirmed car, matched to its coverage row by idMacchina - the only
  // unambiguous identity, since one engine code can span several trims.
  readonly selected = computed<CoverageVehicle | null>(() => {
    const confirmed = this.chat.confirmedCar();
    if (!confirmed) return null;
    return this.coverage.vehicles().find(v => v.idMacchina === confirmed.idMacchina) ?? null;
  });

  // Fault cases first, pictures second. The client is evaluating a
  // diagnostic tool, not a gallery: the first thing it should show is what
  // it is FOR. A photo impresses, but it illustrates - it does not prove.
  sections(car: CoverageVehicle): { key: string; title: string; unit: string; section: CoverageSection }[] {
    const s = this.t();
    const c = car.sections;
    return [
      { key: 'cases',     title: s.sec_cases,     unit: s.unit_cases,     section: c.cases },
      { key: 'photos',    title: s.sec_photos,    unit: s.unit_photos,    section: c.photos },
      { key: 'codes',     title: s.sec_codes,     unit: s.unit_codes,     section: c.faultCodes },
      { key: 'diagrams',  title: s.sec_diagrams,  unit: s.unit_diagrams,  section: c.diagrams },
      { key: 'manual',    title: s.sec_manual,    unit: s.unit_manual,    section: c.manual },
      { key: 'technical', title: s.sec_technical, unit: s.unit_technical, section: c.technical },
    ];
  }

  // One line per vehicle card saying what it actually carries, so the reader
  // picks knowing the four are not equivalent - the Fiat alone has the
  // scanned manual, the BMW has no fault code at all.
  holdings(v: CoverageVehicle): string {
    const s = this.t();
    const c = v.sections;
    return [
      c.cases.total      ? `${c.cases.total} ${s.unit_cases}`           : null,
      c.faultCodes.total ? `${c.faultCodes.total} ${s.unit_codes}`      : null,
      c.photos.total     ? `${c.photos.total} ${s.unit_photos}`         : null,
      c.diagrams.total   ? `${c.diagrams.total} ${s.unit_diagrams}`     : null,
      c.manual.total     ? `${c.manual.total} ${s.unit_manual}`         : null,
    ].filter(Boolean).join(' · ');
  }

  groupLabel(prefix: string): string {
    const s = this.t();
    switch (prefix) {
      case 'P': return s.codes_group_p;
      case 'B': return s.codes_group_b;
      case 'C': return s.codes_group_c;
      case 'U': return s.codes_group_u;
      default:  return s.codes_group_other;
    }
  }

  languageName(code: HelpLanguage): string {
    return LANGUAGE_NAMES[code];
  }

  coverageSuffix(code: HelpLanguage): string {
    const c = this.coverage.forLanguage(code);
    if (!c) return '';

    const s = this.t();
    if (c.manualPages > 0 && c.diagrams > 0) return ` — ${s.coverage_full}`;
    if (c.diagrams > 0) return ` — ${s.coverage_no_manual}`;
    return ` — ${s.coverage_data_only}`;
  }
}
