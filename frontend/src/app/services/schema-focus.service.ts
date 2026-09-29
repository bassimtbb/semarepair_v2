import { Injectable } from '@angular/core';

// Lets a spoken or typed command reach the diagram on screen.
//
// A wiring diagram is rendered by SchemaViewerComponent, one instance per
// diagram, nested inside a technical answer card inside a message. Neither the
// voice service nor the store can see it - and should not: a registry keeps
// the command paths independent of where the component happens to sit in the
// tree.
//
// The target is an interface rather than the component type so this service
// stays free of the component (which imports DomSanitizer and Lucide icons),
// and so the command paths can be tested without rendering a PDF.
export interface SchemaFocusTarget {
  readonly schemaTitle: string;
  isFullscreen(): boolean;
  enterFullscreen(): void;
  exitFullscreen(): void;
}

@Injectable({ providedIn: 'root' })
export class SchemaFocusService {
  // Registration order is creation order, so the last entry is the diagram
  // furthest down the conversation - which is the one "questo schema" means.
  // A mechanic who has scrolled back up to an older diagram is not addressed
  // by this; he has the button.
  private targets: SchemaFocusTarget[] = [];

  register(target: SchemaFocusTarget): void {
    this.targets.push(target);
  }

  unregister(target: SchemaFocusTarget): void {
    this.targets = this.targets.filter(t => t !== target);
  }

  get current(): SchemaFocusTarget | null {
    return this.targets.length > 0 ? this.targets[this.targets.length - 1] : null;
  }

  hasSchema(): boolean {
    return this.current !== null;
  }

  /** Enlarges the most recent diagram and returns its title, or null if there is none. */
  enlarge(): string | null {
    const target = this.current;
    if (!target) return null;
    if (!target.isFullscreen()) target.enterFullscreen();
    return target.schemaTitle;
  }

  /** Restores the most recent diagram to its inline size. */
  shrink(): string | null {
    const target = this.current;
    if (!target) return null;
    if (target.isFullscreen()) target.exitFullscreen();
    return target.schemaTitle;
  }
}
