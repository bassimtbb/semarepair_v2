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
      }

      <div class="tech-source text-muted">{{ sourceLabel }}</div>
    </div>
  `,
  styleUrl: './technical-answer-card.component.css',
})
export class TechnicalAnswerCardComponent {
  @Input({ required: true }) chunk!: TechnicalChunk;

  // The document, then the section within it when they differ - "Fusibili e
  // Relè · Scatola Fusibili - Vano Motore" tells the mechanic both which
  // manual page this is and which box to open. Repeating the heading when it
  // equals the title would just be noise.
  get sourceLabel(): string {
    const parts = [this.chunk.documentTitle, this.chunk.heading]
      .filter((p): p is string => !!p);
    const unique = parts.filter((p, i) => parts.indexOf(p) === i);
    return unique.join(' · ');
  }
}
