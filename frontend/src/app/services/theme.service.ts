import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'semarepair-theme';

// The actual dark/light decision at boot is made synchronously in
// index.html's inline script (runs before Angular loads, avoiding a flash
// of the wrong theme) - this service just becomes the single source of
// truth for changes after that, keeping localStorage and the <html>
// class in sync with each other on every toggle.
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly isDark = signal(document.documentElement.classList.contains('dark'));

  toggle(): void {
    const next = !this.isDark();
    document.documentElement.classList.toggle('dark', next);
    localStorage.setItem(STORAGE_KEY, next ? 'dark' : 'light');
    this.isDark.set(next);
  }
}
