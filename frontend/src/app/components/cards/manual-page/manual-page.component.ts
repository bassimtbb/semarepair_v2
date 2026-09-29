import { Component, ElementRef, Input, OnDestroy, OnInit, ViewChild, signal } from '@angular/core';
import { LucideMaximize2, LucideMinimize2, LucideExternalLink } from '@lucide/angular';
import { SchemaFocusService, SchemaFocusTarget } from '../../../services/schema-focus.service';

// A page of the scanned workshop manual.
//
// The page image IS the answer here, not an illustration of one. Its OCR text
// was indexed so the page could be found; not one character of it is rendered,
// because a character a machine guessed at must never be read as a torque or
// an amperage. What the mechanic reads is the scan, exactly as it was printed.
//
// Hence the badge. Every other card in this conversation carries text the
// client's archive wrote; this one carries a photograph of a magazine page,
// and saying so plainly is the difference between a source and a disguise.
//
// An <img>, not the PDF iframe the diagrams use: this is already a raster
// image, so there is no viewer to inherit and nothing to lose by rendering it
// directly - and it works in Safari, which the PDF frame does not.
@Component({
  selector: 'app-manual-page',
  standalone: true,
  imports: [LucideMaximize2, LucideMinimize2, LucideExternalLink],
  template: `
    <div class="manual" #container [class.fullscreen]="isFullscreen()">
      <div class="manual-bar bg-card-surface border-border">
        <span class="manual-badge">manuale scansionato</span>
        @if (page) {
          <span class="manual-page-no">pag. {{ page }}</span>
        }
        <span class="manual-title text-muted">{{ heading }}</span>
        <div class="manual-actions">
          <a
            class="manual-btn text-muted hover:bg-foreground/8"
            [href]="url"
            target="_blank"
            rel="noopener"
            title="Apri la pagina in una nuova scheda"
          >
            <svg lucideExternalLink [size]="15"></svg>
          </a>
          <button
            type="button"
            class="manual-btn text-muted hover:bg-foreground/8"
            (click)="toggleFullscreen()"
            [title]="isFullscreen() ? 'Esci da schermo intero' : 'Schermo intero'"
          >
            @if (isFullscreen()) {
              <svg lucideMinimize2 [size]="15"></svg>
            } @else {
              <svg lucideMaximize2 [size]="15"></svg>
            }
          </button>
        </div>
      </div>

      <!-- Cliquer la page l'agrandit. Un vrai clic porte une interaction
           utilisateur, donc c'est le seul chemin qui obtient le vrai plein
           ecran du navigateur - la commande vocale, elle, retombe toujours
           sur la classe CSS. -->
      <img
        class="manual-img"
        [src]="url"
        [alt]="heading"
        loading="lazy"
        (click)="toggleFullscreen()"
      />
    </div>
  `,
  styleUrl: './manual-page.component.css',
})
export class ManualPageComponent implements SchemaFocusTarget, OnInit, OnDestroy {
  @Input({ required: true }) assetId!: string;
  @Input() heading = '';

  // Which page of the scan this is. A section spans several pages and they all
  // carry its title, so without this two cards read as the same page shown
  // twice. It is also what a mechanic would write down.
  @Input() page = '';

  readonly isFullscreen = signal(false);

  @ViewChild('container') private container?: ElementRef<HTMLDivElement>;

  constructor(private readonly schemaFocus: SchemaFocusService) {}

  // Registered in the same list as the wiring diagrams, so "ingrandisci" -
  // spoken or typed - enlarges whichever of the two is furthest down the
  // conversation. The mechanic says one word; he should not have to know
  // whether he is looking at a diagram or a manual page.
  ngOnInit(): void { this.schemaFocus.register(this); }
  ngOnDestroy(): void { this.schemaFocus.unregister(this); }

  get schemaTitle(): string { return this.heading; }

  get url(): string {
    return `/assets/manual/${encodeURIComponent(this.assetId)}`;
  }

  toggleFullscreen(): void {
    if (this.isFullscreen() || document.fullscreenElement) this.exitFullscreen();
    else this.enterFullscreen();
  }

  // Same split, and the same reason, as SchemaViewerComponent: a spoken
  // "ingrandisci" said twice must not shrink the page back.
  enterFullscreen(): void {
    const el = this.container?.nativeElement;
    if (!el) return;

    if (typeof el.requestFullscreen !== 'function') {
      this.isFullscreen.set(true);
      return;
    }

    el.requestFullscreen()
      .then(() => this.isFullscreen.set(true))
      .catch(() => this.isFullscreen.set(true)); // refused without a gesture: CSS fallback

    const onChange = () => {
      if (!document.fullscreenElement) {
        this.isFullscreen.set(false);
        document.removeEventListener('fullscreenchange', onChange);
      }
    };
    document.addEventListener('fullscreenchange', onChange);
  }

  exitFullscreen(): void {
    if (document.fullscreenElement) {
      void document.exitFullscreen().catch(() => {});
    }
    this.isFullscreen.set(false);
  }
}
