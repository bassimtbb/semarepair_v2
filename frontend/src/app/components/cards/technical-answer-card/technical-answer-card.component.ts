import { Component, Input } from '@angular/core';
import { SchemaViewerComponent } from '../schema-viewer/schema-viewer.component';
import type { TechnicalChunk } from '../../../models/chat.models';

// Renders one answer to a technical question. Extension v2.
//
// Three shapes behind one component, keyed off `kind`, because that is how
// the backend already models it: a single search tool returns whatever fits
// the question, and what distinguishes a fuse rating from a wiring diagram
// is the chunk's kind, never the model's choice.
//
//   fact     the value is the answer - shown large, with its unit and its
//            reference mark (F04, so the mechanic can find it in the box)
//   legend   a component on a diagram - shows the diagram itself
//   section  a procedure - prose, preserving its line breaks
//
// Every card names where the answer came from, the same transparency rule
// the repair cards follow: an unattributed technical value is exactly the
// kind of confident-looking answer this product refuses to give.
@Component({
  selector: 'app-technical-answer-card',
  standalone: true,
  imports: [SchemaViewerComponent],
  template: `
    <div class="tech-card bg-surface border-border">
      @if (chunk.kind === 'legend' && chunk.assetId) {
        <app-schema-viewer
          [assetId]="chunk.assetId"
          [title]="chunk.heading || chunk.documentTitle || ''"
        />
        @if (chunk.label) {
          <div class="tech-legend-note text-muted">
            @if (chunk.reference) {
              <span class="tech-ref">{{ chunk.reference }}</span>
            }
            {{ chunk.label }}{{ chunk.value ? ' — ' + chunk.value : '' }}
          </div>
        }
      } @else {
        @if (chunk.label || chunk.heading) {
          <div class="tech-label text-foreground">
            @if (chunk.reference && chunk.kind === 'fact') {
              <span class="tech-ref">{{ chunk.reference }}</span>
            }
            {{ chunk.label || chunk.heading }}
          </div>
        }

        @if (chunk.value) {
          <div class="tech-value text-accent">
            {{ chunk.value }}@if (chunk.unit) {<span class="tech-unit text-muted"> {{ chunk.unit }}</span>}
          </div>
        }

        @if (chunk.body) {
          <div class="tech-body text-foreground">{{ chunk.body }}</div>
        }

        @if (contextLabel) {
          <div class="tech-context text-foreground">{{ contextLabel }}</div>
        }
      }

      <div class="tech-source text-muted">{{ sourceLabel }}</div>
    </div>
  `,
  styleUrl: './technical-answer-card.component.css',
})
export class TechnicalAnswerCardComponent {
  @Input({ required: true }) chunk!: TechnicalChunk;

  // Where the answer sits, promoted out of the grey source line.
  //
  // Asked "dove si trova il fusibile F17", the card led with 5 (A) - the
  // rating - while the actual location, "Scatola Fusibili - Vano Motore",
  // was buried in small grey type behind the document title. Correct, and
  // answering a different question than the one asked.
  //
  // Kind cannot tell us whether the mechanic wanted the rating or the place,
  // and guessing from the wording is the mistake this codebase has already
  // made once. So both are shown, each legible: the value large, the
  // enclosure under it. For a torque or an engine spec the heading is a group
  // name that merely repeats the document, and is dropped instead.
  get contextLabel(): string | null {
    const heading = this.chunk.heading?.trim();
    if (!heading) return null;
    const title = this.chunk.documentTitle?.trim() ?? '';
    return heading.toLowerCase() === title.toLowerCase() ? null : heading;
  }

  // Just the document now - the section within it has its own line above.
  get sourceLabel(): string {
    return this.chunk.documentTitle?.trim() || this.chunk.heading?.trim() || '';
  }
}
