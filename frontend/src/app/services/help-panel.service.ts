import { Injectable, signal } from '@angular/core';

// Open/closed state of the help drawer, held here rather than in a
// component because two places drive it: the "?" button in the global
// header, and the drawer's own close button and Escape key.
//
// The drawer opens only when asked. It briefly also opened by itself on a
// browser's first visit, remembered in localStorage; that was removed, so
// nothing greets the client before he has done anything. If it comes back,
// it belongs here rather than in the component - the header button and the
// drawer both already read this signal.
@Injectable({ providedIn: 'root' })
export class HelpPanelService {
  readonly isOpen = signal(false);

  toggle(): void { this.isOpen.update(open => !open); }
  open(): void { this.isOpen.set(true); }
  close(): void { this.isOpen.set(false); }
}
