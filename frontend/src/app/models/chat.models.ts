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

// Extension v2 - an answer to a technical question about the vehicle, as
// opposed to a repair document. Mirrors ChatService.Models.TechnicalChunk.
export interface TechnicalChunk {
  idDocumento: string;
  language: string;
  // 'fact'    a value: fuse rating, torque, bulb type, engine spec
  // 'legend'  a component on a wiring diagram - carries assetId
  // 'section' a prose procedure
  // Drives how the card renders; it is also why a single backend tool can
  // answer three different kinds of question.
  kind: 'fact' | 'legend' | 'section' | string;
  heading?: string | null;
  label?: string | null;
  value?: string | null;
  unit?: string | null;
  reference?: string | null;
  body?: string | null;
  // Id of the PDF showing this diagram, served at /assets/pdf/{assetId}.
  assetId?: string | null;
  documentTitle?: string | null;
}

// One SSE "data:" event from POST /api/chat/stream.
export interface ChatResponse {
  phase: 'identification' | 'chat';
  found: boolean;
  message?: string | null;
  carMatches: CarOption[];
  cases: CaseSummary[];
  technicalChunks: TechnicalChunk[];
}

export type ChatRole = 'user' | 'assistant';

export interface ChatMessage {
  id: string;
  role: ChatRole;
  text?: string | null;
  carMatches?: CarOption[];
  cases?: CaseSummary[];
  technicalChunks?: TechnicalChunk[];
  isStreaming?: boolean;
  // Index into cases[] of the document currently expanded in the selection
  // list. undefined/null = compact list visible. Set by selectDocument() and
  // cleared by clearDocumentSelection() in ChatStore.
  selectedCaseIndex?: number | null;
}
