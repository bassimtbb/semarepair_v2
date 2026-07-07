// Mirrors services/chat/Models/{ChatRequest,ChatResponse}.cs exactly.
// ASP.NET Core serializes with JsonSerializerDefaults.Web -> camelCase.

export interface ChatRequest {
  sessionId: string;
  message: string;
  // The specific car the mechanic picked (idMacchina) - the only
  // unambiguous way to confirm a car. codiceMotore+marca alone can match
  // several trims at once (e.g. one engine code spans 5 IVECO Daily III
  // variants), so they're sent only as a fallback for the backend, never
  // used here to pick a row ourselves.
  confirmedCarId?: string | null;
  confirmedCodiceMotore?: string | null;
  confirmedMarca?: string | null;
  language: string;
}

export interface CarOption {
  idMacchina: string;
  marca: string;
  modello: string;
  motorizzazione?: string | null;
  codiceMotore: string;
  alimentazione?: string | null;
  annoInizio?: number | null;
  annoFine?: number | null;
  kw?: number | null;
  cavalli?: number | null;
}

export interface CaseSummary {
  idDocumento: string;
  sigla: string;
  titolo: string;
  impianto: string;
  dispositivo: string;
  anomalia: string;
  causa: string;
  intervento: string;
  procedura: string;
  nota: string;
  reliability: number;
  language: string;
  dtcCodes: FaultCodeInfo[];
  foundViaSharedEngine: boolean;
}

export interface FaultCodeInfo {
  code: string;
  description: string | null;
}

// One SSE "data:" event from POST /api/chat/stream.
export interface ChatResponse {
  phase: 'identification' | 'chat';
  found: boolean;
  message?: string | null;
  carMatches: CarOption[];
  cases: CaseSummary[];
}

export type ChatRole = 'user' | 'assistant';

export interface ChatMessage {
  id: string;
  role: ChatRole;
  text?: string | null;
  carMatches?: CarOption[];
  cases?: CaseSummary[];
  isStreaming?: boolean;
}
