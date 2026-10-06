import { Injectable, computed, signal } from '@angular/core';
import type { HelpLanguage } from './help-strings';

// Same relative-path convention as ChatApiService: nginx proxies
// /api/search/* to search-service in prod, proxy.conf.json forwards it
// under `ng serve`.
const COVERAGE_URL = '/api/search/coverage';

export interface CoverageSection {
  total: number;
  examples: string[];
}

export interface CoverageSections {
  cases: CoverageSection;
  photos: CoverageSection;
  faultCodes: CoverageSection;
  diagrams: CoverageSection;
  manual: CoverageSection;
  technical: CoverageSection;
}

export interface CoverageVehicle {
  idMacchina: string;
  marca?: string | null;
  modello?: string | null;
  motorizzazione?: string | null;
  annoInizio?: number | null;
  annoFine?: number | null;
  alimentazione?: string | null;
  kw?: number | null;
  cavalli?: number | null;
  codiceMotore?: string | null;
  query: string;
  sections: CoverageSections;
}

export interface CoverageLanguage {
  code: string;
  facts: number;
  procedures: number;
  manualPages: number;
  diagrams: number;
}

export interface Coverage {
  vehicles: CoverageVehicle[];
  languages: CoverageLanguage[];
}

// The four DTC families, in the order SAE J2012 defines them. The prefix is
// the only part of a code that carries meaning without a lookup, so the list
// is grouped by it - a mechanic chasing an ABS fault reads the C block and
// ignores the rest.
export type FaultCodeGroup = { prefix: string; codes: string[] };

// What the archive holds, per vehicle, fetched once per language.
//
// None of it is a constant. The counts, the section contents and the
// suggested questions all come from the archive, which is why the day three
// cars arrived nothing in the drawer had to be rewritten - and why the
// numbers it shows are the ones the search can actually reach.
@Injectable({ providedIn: 'root' })
export class CoverageService {
  readonly coverage = signal<Coverage | null>(null);

  private loadedLanguage: string | null = null;

  // Failure is silent on purpose. This decorates a help panel; if the
  // endpoint is unreachable the panel omits what it cannot state, which is
  // better than an error banner over a chat that works perfectly well.
  load(language: string): void {
    if (this.loadedLanguage === language) return;
    this.loadedLanguage = language;

    fetch(`${COVERAGE_URL}?lang=${encodeURIComponent(language)}`)
      .then(res => (res.ok ? res.json() : null))
      .then((data: Coverage | null) => { if (data) this.coverage.set(data); })
      .catch(() => { this.loadedLanguage = null; });
  }

  readonly vehicles = computed(() => this.coverage()?.vehicles ?? []);

  forLanguage(lang: HelpLanguage): CoverageLanguage | null {
    return this.coverage()?.languages.find(l => l.code === lang) ?? null;
  }

  // "FIAT 500 1.2 8v · 2007-2021 · Benzina · 51 kW / 70 CV" - the same shape
  // as the confirmed-car badge in the header, so the two agree on sight.
  // annoFine 9999 means "still in production", the convention already used
  // by AppComponent.carDetails and RepairOrchestrator.
  label(v: CoverageVehicle): string {
    const head = [v.marca, v.modello, v.motorizzazione].filter(Boolean).join(' ');
    const parts: string[] = [];

    if (v.annoInizio) parts.push(`${v.annoInizio}–${v.annoFine === 9999 ? '' : v.annoFine ?? ''}`);
    if (v.alimentazione) parts.push(v.alimentazione);
    if (v.codiceMotore) parts.push(v.codiceMotore);

    return parts.length ? `${head} · ${parts.join(' · ')}` : head;
  }

  // Grouped by first letter, families in SAE order, anything unexpected kept
  // at the end rather than dropped - a code the archive holds must appear in
  // this list whatever it looks like, or the list stops being the answer to
  // "what can I ask".
  groupFaultCodes(codes: string[]): FaultCodeGroup[] {
    if (codes.length === 0) return [];

    const order = ['P', 'B', 'C', 'U'];
    const buckets = new Map<string, string[]>();

    for (const code of codes) {
      const prefix = code.charAt(0).toUpperCase();
      const key = order.includes(prefix) ? prefix : '?';
      (buckets.get(key) ?? buckets.set(key, []).get(key)!).push(code);
    }

    return [...order, '?']
      .filter(p => buckets.has(p))
      .map(prefix => ({ prefix, codes: buckets.get(prefix)! }));
  }
}
