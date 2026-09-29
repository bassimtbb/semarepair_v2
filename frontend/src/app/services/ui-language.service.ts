import { Injectable, signal } from '@angular/core';

export type UiLanguage = 'it' | 'en' | 'fr' | 'pt' | 'es';
const STORAGE_KEY = 'semarepair-ui-language';
const LANGUAGES: UiLanguage[] = ['it', 'en', 'fr', 'pt', 'es'];

@Injectable({ providedIn: 'root' })
export class UiLanguageService {
  readonly lang = signal<UiLanguage>(this.readInitialLanguage());

  setLanguage(language: string): void {
    if (!LANGUAGES.includes(language as UiLanguage)) return;
    const next = language as UiLanguage;
    this.lang.set(next);
    try { localStorage.setItem(STORAGE_KEY, next); } catch { /* storage can be disabled */ }
  }

  private readInitialLanguage(): UiLanguage {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved && LANGUAGES.includes(saved as UiLanguage)) return saved as UiLanguage;
    } catch { /* storage can be disabled */ }
    const browser = typeof navigator === 'undefined' ? '' : navigator.language.slice(0, 2).toLowerCase();
    return LANGUAGES.includes(browser as UiLanguage) ? browser as UiLanguage : 'it';
  }
}
