import { Component, ElementRef, Input, OnDestroy, OnInit, ViewChild, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { LucideMaximize2, LucideMinimize2, LucideExternalLink } from '@lucide/angular';
import { SchemaFocusService, SchemaFocusTarget } from '../../../services/schema-focus.service';

// Shows a wiring diagram inline in the conversation, with a fullscreen
// control. Extension v2 (docs/Architecture_Extension_v2.md).
//
// An <iframe> onto the browser's own PDF viewer, not pdf.js. The diagrams
// are single-page vector drawings with no extractable text (verified: six of
// the seven yield zero words), so there is nothing to search, select or
// annotate - all the things a JS renderer would buy. What matters is that
// the drawing is legible and can be enlarged, and the native viewer already
// does both, with zoom and print, for no dependency.
//
// The honest trade-off: this is the most browser-dependent piece of the
// extension. Chrome, Edge and Firefox on desktop render PDFs in an iframe;
// iOS Safari does not, and shows a blank frame. The "open in a tab" control
// is the fallback for that, which is why it is always visible rather than
// hidden behind the fullscreen button.
@Component({
  selector: 'app-schema-viewer',
  standalone: true,
  imports: [LucideMaximize2, LucideMinimize2, LucideExternalLink],
  template: `
    <div class="schema" #container [class.fullscreen]="isFullscreen()">
      <div class="schema-bar bg-card-surface border-border">
        <span class="schema-title text-accent">{{ title }}</span>
        <div class="schema-actions">
          <a
            class="schema-btn text-muted hover:bg-foreground/8"
            [href]="rawUrl"
            target="_blank"
            rel="noopener"
            title="Apri in una nuova scheda"
            aria-label="Apri lo schema in una nuova scheda"
          >
            <svg lucideExternalLink [size]="15"></svg>
          </a>
          <button
            type="button"
            class="schema-btn text-muted hover:bg-foreground/8"
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

      <iframe
        class="schema-frame"
        [src]="safeUrl"
        [title]="title"
        loading="lazy"
      ></iframe>
    </div>
  `,
  styleUrl: './schema-viewer.component.css',
})
export class SchemaViewerComponent implements SchemaFocusTarget, OnInit, OnDestroy {
  @Input({ required: true }) assetId!: string;
  @Input() title = '';

  readonly isFullscreen = signal(false);

  @ViewChild('container') private container?: ElementRef<HTMLDivElement>;

  constructor(
    private readonly sanitizer: DomSanitizer,
    private readonly schemaFocus: SchemaFocusService,
  ) {}

  // Registered so "ingrandisci questo schema", spoken or typed, can reach
  // this instance without the voice service or the store knowing where the
  // component sits. See SchemaFocusService.
  ngOnInit(): void { this.schemaFocus.register(this); }
  ngOnDestroy(): void { this.schemaFocus.unregister(this); }

  // What the command path reads back to the mechanic ("Ho ingrandito lo
  // schema Airbag Siemens MY99"), so he knows WHICH diagram grew when the
  // conversation holds several.
  get schemaTitle(): string { return this.title; }

  get rawUrl(): string {
    return `/assets/pdf/${encodeURIComponent(this.assetId)}`;
  }

  // The native viewer's own parameters. The toolbar is redundant at this size -
  // the bar above already offers the two actions that matter.
  //
  // view=Fit, not FitH: these are landscape A4 drawings (841x595), so fitting
  // the WIDTH of a chat-sized frame renders them ~650px tall and cuts the
  // bottom third off - which is what the first version did. Fit shows the
  // whole schematic at once. It is small, and that is the intended division
  // of labour: the inline view is for recognising the right diagram, the
  // fullscreen button is for working from it.
  //
  // Both are hints; a browser that ignores them still renders the drawing.
  get safeUrl(): SafeResourceUrl {
    return this.sanitizer.bypassSecurityTrustResourceUrl(
      `${this.rawUrl}#toolbar=0&navpanes=0&view=Fit`,
    );
  }

  // The Fullscreen API rather than a CSS-only overlay: a PDF iframe keeps
  // its own scroll and zoom state across a real fullscreen transition, but
  // is reloaded when it is moved in the DOM - which is what a CSS overlay
  // built from a separate element would do, losing the reader's place.
  //
  // Falls back to the CSS class alone when the API is unavailable or
  // refused, so the control never does nothing.
  toggleFullscreen(): void {
    if (this.isFullscreen() || document.fullscreenElement) this.exitFullscreen();
    else this.enterFullscreen();
  }

  // Split out of toggleFullscreen so a command can be explicit. "Ingrandisci"
  // said twice must not shrink the diagram, which a toggle would do - the
  // mechanic cannot see whether the first one worked.
  //
  // This is also the path a voice command takes, and the reason the catch
  // below matters more than it looks: the Fullscreen API requires a recent
  // user gesture, and a speech-recognition result is not one, so Chrome
  // rejects the promise. The CSS class then does the work on its own. The
  // visible difference is the browser's own tab bar staying put; pressing F11
  // beforehand removes even that.
  enterFullscreen(): void {
    const el = this.container?.nativeElement;
    if (!el) return;

    if (typeof el.requestFullscreen !== 'function') {
      this.isFullscreen.set(true);
      return;
    }

    el.requestFullscreen()
      .then(() => this.isFullscreen.set(true))
      .catch(() => this.isFullscreen.set(true)); // CSS-only fallback

    // Covers Escape and the browser's own exit control, which never call
    // exitFullscreen() - without this the class would stay applied and
    // leave the viewer stuck at overlay size.
    const onChange = () => {
      if (!document.fullscreenElement) {
        this.isFullscreen.set(false);
        document.removeEventListener('fullscreenchange', onChange);
      }
    };
    document.addEventListener('fullscreenchange', onChange);
  }

  exitFullscreen(): void {
    // Only the real API needs unwinding; the fallback is the class alone, and
    // exitFullscreen() throws when nothing is actually fullscreen.
    if (document.fullscreenElement) {
      void document.exitFullscreen().catch(() => {});
    }
    this.isFullscreen.set(false);
  }
}
