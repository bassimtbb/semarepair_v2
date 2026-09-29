import { Injectable, computed, signal } from '@angular/core';
import type { HelpLanguage } from './help-strings';

// Same relative-path convention as ChatApiService: nginx proxies
// /api/search/* to search-service in prod, proxy.conf.json forwards it
// under `ng serve`.
const COVERAGE_URL = '/api/search/coverage';

export interface CoverageVehicle {
  marca?: string | null;
  modello?: string | null;
  motorizzazione?: string | null;
  annoInizio?: number | null;
  annoFine?: number | null;
  alimentazione?: string | null;
  kw?: number | null;
  cavalli?: number | null;
  codiceMotore?: string | null;
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
  repairDocuments: number;
  languages: CoverageLanguage[];
  faultCodes: string[];
}

// The four DTC families, in the order SAE J2012 defines them. The prefix is
// the only part of a code that carries meaning without a lookup, so the list
// is grouped by it rather than left as one run of 139 - a mechanic looking
// for an ABS fault reads the C block and ignores the rest.
export type FaultCodeGroup = { prefix: string; codes: string[] };

// What the archive holds, fetched once per page load and cached in a signal.
//
// The drawer could have carried these numbers as constants - the obvious
// shortcut, and wrong within the week. Five wiring diagrams are missing
// their drawing and have been asked for; the day they land, an ingestion
// run has to be enough to correct what the interface claims.
@Injectable({ providedIn: 'root' })
export class CoverageService {
  readonly coverage = signal<Coverage | null>(null);

  private started = false;

  // Failure is silent on purpose. This decorates a help panel; if the
  // endpoint is unreachable the panel omits the figures, which is better
  // than an error banner over a chat that works perfectly well.
  load(): void {
    if (this.started) return;
    this.started = true;

    fetch(COVERAGE_URL)
      .then(res => (res.ok ? res.json() : null))
      .then((data: Coverage | null) => { if (data) this.coverage.set(data); })
      .catch(() => {});
  }

  forLanguage(lang: HelpLanguage): CoverageLanguage | null {
    return this.coverage()?.languages.find(l => l.code === lang) ?? null;
  }

  // "FIAT 500 1.2 8v · 2007-2021 · Benzina" - the same shape as the
  // confirmed-car badge in the header, so the two agree on sight.
  // annoFine 9999 means "still in production", the convention already used
  // by AppComponent.carDetails and RepairOrchestrator.
  vehicleLabel(v: CoverageVehicle): string {
    const head = [v.marca, v.modello, v.motorizzazione].filter(Boolean).join(' ');
    const parts: string[] = [];

    if (v.annoInizio) {
      const to = v.annoFine === 9999 ? '' : v.annoFine ?? '';
      parts.push(`${v.annoInizio}–${to}`);
    }
    if (v.alimentazione) parts.push(v.alimentazione);

    return parts.length ? `${head} · ${parts.join(' · ')}` : head;
  }

  // What the mechanic types to select the car: brand, model and trim, with
  // no years or fuel. Measured against the live stack - "FIAT 500 1.2 8v"
  // returns exactly one match, so the suggestion leads straight to a single
  // vehicle card rather than a list to disambiguate.
  vehicleQuery(v: CoverageVehicle): string {
    return [v.marca, v.modello, v.motorizzazione].filter(Boolean).join(' ');
  }

  // Grouped by first letter, families in SAE order, anything unexpected kept
  // at the end rather than dropped - a code the archive holds must appear in
  // this list whatever it looks like, or the list stops being the answer to
  // "what can I ask".
  readonly faultCodeGroups = computed<FaultCodeGroup[]>(() => {
    const codes = this.coverage()?.faultCodes ?? [];
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
  });
}
