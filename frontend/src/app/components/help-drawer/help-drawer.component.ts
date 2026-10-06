import { Component, EventEmitter, HostListener, OnInit, Output, computed, effect } from '@angular/core';
import {
  LucideX, LucideStar, LucideMessageCircle, LucideCar, LucideWrench, LucideImage,
  LucideHash, LucideZap, LucideBookOpen, LucideGauge, LucideSparkles, LucideSearch,
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
    LucideHash, LucideZap, LucideBookOpen, LucideGauge, LucideSparkles, LucideSearch, LucideSearch,
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
                              (click)="suggest.emit(s.phrase(q))">{{ s.phrase(q) }}</button>
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

          <!-- ...ou commencer par une question. Chaque puce porte sa voiture,
               donc un clic confirme le vehicule ET pose la question dans le
               meme tour. Sans ce porteur, une question technique ne pourrait
               que repondre "quel vehicule ?" - la regle 1 exige un code
               moteur - et on proposerait une suggestion qui ne marche pas. -->
          @for (p of pooled(); track p.key) {
            @if (p.items.length) {
              <section class="help-section">
                <div class="help-section-head text-accent">
                  @switch (p.key) {
                    @case ('cases')     { <svg lucideWrench [size]="15"></svg> }
                    @case ('photos')    { <svg lucideImage [size]="15"></svg> }
                    @case ('codes')     { <svg lucideHash [size]="15"></svg> }
                    @case ('technical') { <svg lucideGauge [size]="15"></svg> }
                  }
                  <span>{{ p.title }}</span>
                </div>
                <div class="help-chips">
                  @for (q of p.items; track q) {
                    <button type="button"
                            class="help-chip bg-surface border-border text-foreground hover:bg-foreground/8"
                            (click)="suggest.emit(q)">{{ q }}</button>
                  }
                </div>
                <p class="help-hint text-muted">{{ t().try_hint }}</p>
              </section>
            }
          }

          <!-- Et la liste complete, repliee. Un testeur qui tape un code au
               hasard recoit un "non trouve" parfaitement correct et en conclut
               que le produit ne marche pas - c'est arrive au premier client,
               avec P1030. La voiture est nommee au-dessus de ses codes parce
               que les trois n'ont pas les memes : 139, 34 et 3. -->
          @if (codeTotal() > 0) {
            <section class="help-section">
              <details class="help-codes">
                <summary class="help-section-head text-accent">
                  <svg lucideHash [size]="15"></svg>
                  <span>{{ t().codes_all_title }}</span>
                  <span class="help-codes-count text-muted">{{ t().codes_all_count(codeTotal()) }}</span>
                </summary>

                @for (v of coverage.vehicles(); track v.idMacchina) {
                  <div class="help-codes-group">
                    <div class="help-codes-label text-foreground">{{ v.marca }} {{ v.modello }} — {{ v.sections.faultCodes.total || t().codes_none }}</div>
                    @for (group of coverage.groupFaultCodes(v.sections.faultCodes.examples); track group.prefix) {
                      <div class="help-codes-label text-muted">{{ groupLabel(group.prefix) }}</div>
                      <div class="help-codes-grid">
                        @for (code of group.codes; track code) {
                          <button type="button"
                                  class="help-code bg-surface border-border text-foreground hover:bg-foreground/8"
                                  (click)="suggest.emit(code)">{{ code }}</button>
                        }
                      </div>
                    }
                  </div>
                }

                <p class="help-hint text-muted">{{ t().codes_hint }}</p>
              </details>
            </section>
          }
        }

        <!-- Comment la recherche travaille. Dit parce que le comportement
             n'est pas celui d'une recherche par mots-cles, et qu'un
             mecanicien qui croit taper des mots-cles tape autrement - et
             prend un "non trouve" parfaitement correct pour une panne. -->
        <section class="help-section">
          <div class="help-section-head text-accent">
            <svg lucideSearch [size]="15"></svg>
            <span>{{ t().how_title }}</span>
          </div>
          <ul class="help-how text-foreground">
            <li>{{ t().how_meaning }}</li>
            @if (sharedCodeCount() > 0) {
              <li>{{ t().how_shared(sharedCodeCount(), coverage.vehicles().length) }}</li>
            }
            <li>{{ t().how_verbatim }}</li>
            <li>{{ t().how_refuses }}</li>
          </ul>
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
  // phrase() turns what the archive stores into what a mechanic types.
  //
  // An anomalia is already a full sentence - the mechanic's own words,
  // written down by whoever filed the repair - so it goes through
  // untouched. A chunk heading is a label: "CLIMATIZZAZIONE" typed on its
  // own returns nothing at all, measured, because one upper-case word sits
  // too far from the chunk for the vector and reads as a system name to the
  // router. Those get a stem.
  sections(car: CoverageVehicle): {
    key: string; title: string; unit: string;
    section: CoverageSection; phrase: (s: string) => string;
  }[] {
    const s = this.t();
    const c = car.sections;
    const asIs = (x: string) => x;

    // Lower-cased on the way in. A heading stored as "CLIMATIZZAZIONE" reads
    // to the router as a SYSTEM name, and it routed to the system search,
    // which correctly found no repair sheet and asked for clarification -
    // measured, through the chat, with the stem already in place. The word
    // "tecnici" in the stem and a lower-case subject both point at the
    // technical search instead; together they held on every heading tried.
    const data = (x: string) => s.ask_data(x.toLocaleLowerCase());
    return [
      { key: 'cases',     title: s.sec_cases,     unit: s.unit_cases,     section: c.cases,      phrase: asIs },
      { key: 'photos',    title: s.sec_photos,    unit: s.unit_photos,    section: c.photos,     phrase: data },
      { key: 'codes',     title: s.sec_codes,     unit: s.unit_codes,     section: c.faultCodes, phrase: asIs },
      { key: 'diagrams',  title: s.sec_diagrams,  unit: s.unit_diagrams,  section: c.diagrams,   phrase: s.ask_diagram },
      { key: 'manual',    title: s.sec_manual,    unit: s.unit_manual,    section: c.manual,     phrase: data },
      { key: 'technical', title: s.sec_technical, unit: s.unit_technical, section: c.technical,  phrase: data },
    ];
  }

  // The same four kinds of question, before any vehicle is confirmed, drawn
  // across the whole archive - one per car so the four stay visible rather
  // than the richest one filling the list.
  //
  // Diagrams and manual are left out here on purpose: only two cars have
  // diagrams and one has the manual, so pooling them would read as a
  // property of the archive rather than of a particular vehicle. They
  // appear once a car is chosen, where they belong.
  // What can be asked BEFORE a vehicle is chosen - and only that.
  //
  // Measured, with no car confirmed: a symptom comes back with the vehicles
  // whose documentation covers it, and so does a fault code. A technical
  // question comes back "which vehicle?", because SearchTechnicalInfo needs
  // an engine code and Rule 1 will not answer without one. So technical
  // data and photos are not offered here - a suggestion that can only earn
  // a clarifying question is worse than no suggestion.
  //
  // They appear in full once a car is chosen, which is the flow the whole
  // interface is built on: say the symptom, read which cars carry it, pick
  // the one in the workshop.
  // Every vehicle is listed, including the one with none - its row says so
  // in words. Leaving it out was the first attempt, and an absence has to be
  // noticed before it says anything.
  // Codes carried by more than one vehicle. The number is the point of the
  // sentence it feeds - "the same code can appear on several cars" means
  // nothing without saying how many actually do here.
  readonly sharedCodeCount = computed(() =>
    this.coverage.coverage()?.shared?.faultCodes?.examples?.length ?? 0,
  );

  readonly codeTotal = computed(() =>
    this.coverage.vehicles().reduce((n, v) => n + v.sections.faultCodes.total, 0),
  );

  readonly pooled = computed(() => {
    const s = this.t();
    const cov = this.coverage.coverage();
    const vehicles = this.coverage.vehicles();

    // Shared questions first - they are the ones that answer with SEVERAL
    // vehicles, which is the step the whole interface exists for. The rest
    // fills up to four, one per car, so every vehicle stays visible.
    const build = (
      shared: string[] | undefined,
      get: (v: CoverageVehicle) => CoverageSection,
    ) => [...new Set([...(shared ?? []), ...vehicles.flatMap(v => get(v).examples.slice(0, 1))])]
          .slice(0, 4);

    return [
      { key: 'cases', title: s.sec_cases,
        items: build(cov?.shared?.cases?.examples, v => v.sections.cases) },
      { key: 'codes', title: s.sec_codes,
        items: build(cov?.shared?.faultCodes?.examples, v => v.sections.faultCodes) },
    ];
  });

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
