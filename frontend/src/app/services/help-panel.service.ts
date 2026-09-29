import { Injectable, signal } from '@angular/core';

const SEEN_KEY = 'semarepair-help-seen';

// Open/closed state of the help drawer, held here rather than in a
// component because two places drive it: the "?" button in the global
// header, and the drawer's own close button and Escape key.
@Injectable({ providedIn: 'root' })
export class HelpPanelService {
  readonly isOpen = signal(false);

  toggle(): void { this.isOpen.update(open => !open); }
  open(): void { this.isOpen.set(true); this.markSeen(); }
  close(): void { this.isOpen.set(false); }

  // Opens by itself the first time this browser sees the app, and never
  // again. Someone handed a link has to be told this is a demo carrying
  // one vehicle before they test it, without hunting for a button; the
  // same person returning five minutes later does not need telling twice.
  //
  // If localStorage is unavailable the read throws and it counts as a
  // first visit - the drawer opens every time, which is the harmless way
  // to be wrong here.
  openOnFirstVisit(): void {
    let seen = false;
    try {
      seen = localStorage.getItem(SEEN_KEY) === '1';
    } catch { /* storage can be disabled */ }
    if (!seen) this.open();
  }

  private markSeen(): void {
    try {
      localStorage.setItem(SEEN_KEY, '1');
    } catch { /* storage can be disabled - the drawer offers itself again */ }
  }
}
