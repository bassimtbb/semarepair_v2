import { Injectable } from '@angular/core';
import type { UsageLogFilters, UsageLogPage, UsageSummary, UsageTimeSeriesPoint } from '../models/usage.models';

// Relative path: nginx proxies /api/usage/* to search-service in prod;
// `ng serve`'s dev proxy forwards the same path locally - same convention
// as ChatApiService.
const BASE_URL = '/api/usage';

// nginx guards /api/usage/* with a shared-secret header (see
// nginx/nginx.conf, docs/log-dashboard.md section 8) - a deliberate
// deterrent, not strong security, since this key lives in a public
// static bundle/localStorage rather than anything truly private. Stored
// in localStorage (not baked into the JS bundle at build time) so the
// secret isn't sitting in plain sight in the app's own source - the
// mechanic-facing chat UI never needs it at all, only whoever opens
// /usage is ever prompted.
const STORAGE_KEY = 'usageDashboardKey';

@Injectable({ providedIn: 'root' })
export class UsageApiService {
  async getSummary(from?: string, to?: string): Promise<UsageSummary> {
    return this.getJson<UsageSummary>('/summary', { from, to });
  }

  async getTimeSeries(bucket: 'hour' | 'day', from?: string, to?: string): Promise<UsageTimeSeriesPoint[]> {
    return this.getJson<UsageTimeSeriesPoint[]>('/timeseries', { bucket, from, to });
  }

  async getLogs(filters: UsageLogFilters, page: number, pageSize: number): Promise<UsageLogPage> {
    return this.getJson<UsageLogPage>('/logs', { ...filters, page, pageSize });
  }

  private async getJson<T>(path: string, params: Record<string, string | number | null | undefined>): Promise<T> {
    const query = new URLSearchParams();
    for (const [key, value] of Object.entries(params)) {
      if (value !== null && value !== undefined && value !== '') query.set(key, String(value));
    }
    const url = `${BASE_URL}${path}${query.size > 0 ? `?${query}` : ''}`;

    const res = await fetch(url, { headers: { 'X-Usage-Key': this.getOrPromptKey() } });

    if (res.status === 403) {
      // Wrong/stale key - clear it so the next call re-prompts instead of
      // failing silently forever with a key that's never going to work.
      localStorage.removeItem(STORAGE_KEY);
      throw new Error('Chiave di accesso errata. Ricarica la pagina per riprovare.');
    }
    if (!res.ok) {
      throw new Error(`Usage request failed: ${res.status} ${res.statusText}`);
    }
    return res.json() as Promise<T>;
  }

  private getOrPromptKey(): string {
    let key = localStorage.getItem(STORAGE_KEY);
    if (!key) {
      key = window.prompt('Chiave di accesso alla dashboard utilizzo Gemini:') ?? '';
      localStorage.setItem(STORAGE_KEY, key);
    }
    return key;
  }
}
