import { Injectable, computed, effect, signal, untracked } from '@angular/core';
import { ChatApiService } from './chat-api.service';
import { ChatStore } from './chat-store.service';
import { SilenceDetector } from './silence-detector';
import { SpeechService } from './speech/speech.service';
import { VoiceCarSelectionService } from './voice-car-selection.service';
import { t, tCarOption, tCarSelectionPrefix, tCaseOption, tCaseTooMany, tFoundNCases,
         tTechFact, tTechMoreResults, tTechSchema, tTechOfferProcedure } from './voice-strings';
import { stripMarkdown } from '../utils/markdown';
import type { CaseSummary, ChatResponse, TechnicalChunk } from '../models/chat.models';

export type VoiceState = 'idle' | 'listening' | 'transcribing' | 'waiting_response' | 'speaking';
export type VoiceEngine = 'web' | 'google';

// §7 / §5.8 buildSpokenText — maps raw ChatResponse fields to spoken text.
// RULE: never calls Gemini, never rewrites causa/intervento. Only §7 wrapper
// strings are added. This function is the sole source of TTS content.
//
// Order strictly follows §5.8:
//   1. §5.6 Rule 8 cross-brand guard — MUST be first (ask-first contract)
//   2. Car selection (§5.4)
//   3. Found document (§5.1 single / §5.2 multi)
//   4. Everything else: speak r.message verbatim (Rule 8b/8c/9/10/not_found)
//
// NOTE: foundViaSharedEngine responses are intercepted in handleResponseReady()
// BEFORE this function is called, so the §5.6 guard here is a safety net only.
// The consent gate (including causa/intervento reveal) lives in commitPendingTranscript
// / speakStoredConsent, not here.
// r.message is Gemini-written and may carry markdown, which speech engines
// read aloud ("asterisk asterisk") or stumble over. Applied only to the
// message field - never to causa/intervento, which is database content the
// RULE above requires be spoken verbatim.
function spokenMessage(message: string | null | undefined): string | null {
  return message ? stripMarkdown(message) : null;
}

export function buildSpokenText(r: ChatResponse, lang: string): string | null {
  // §5.6 Rule 8 safety net: if a shared-engine response somehow reaches here,
  // speak only the disclosure. The real gate is in handleResponseReady().
  if (r.cases?.[0]?.foundViaSharedEngine === true) {
    return spokenMessage(r.message);
  }

  // §5.4 Car selection
  if (r.carMatches.length > 5) {
    return t(lang, 'car_selection_too_many');
  }
  if (r.carMatches.length > 0) {
    const parts = [tCarSelectionPrefix(lang, r.carMatches.length)];
    r.carMatches.forEach((car, i) => {
      const label = [car.marca, car.modello, car.motorizzazione].filter(Boolean).join(' ');
      parts.push(tCarOption(lang, i + 1, label));
    });
    parts.push(t(lang, 'car_selection_prompt'));
    return parts.join(' ');
  }

  // §5.1 / §5.2 Found document — message is null for clean results
  // (BuildFormatting: "message is null when the result speaks for itself")
  if (r.found && r.cases.length > 0) {
    if (r.cases.length === 1) {
      // §5.1: single case — read causa + intervento verbatim
      const c = r.cases[0];
      const parts: string[] = [];
      if (c.causa)      parts.push(`${t(lang, 'causa_prefix')} ${c.causa}`);
      if (c.intervento) parts.push(`${t(lang, 'intervento_prefix')} ${c.intervento}`);
      return parts.length > 0 ? parts.join(' ') : null;
    }
    // §5.2: multiple cases
    if (r.cases.length > 5) {
      return tCaseTooMany(lang, r.cases.length);
    }
    // 2–5 cases: numbered list, then prompt
    const parts = [tFoundNCases(lang, r.cases.length)];
    r.cases.forEach((c, i) => {
      parts.push(tCaseOption(lang, i + 1, c.dispositivo || c.titolo));
    });
    parts.push(t(lang, 'case_selection_prompt'));
    return parts.join(' ');
  }

  // Extension v2: technical answers. Without this branch they fall through
  // to r.message below and the mechanic hears only "here is the technical
  // information found" - the framing, never the answer. Which is the worst
  // place for that gap: he turned the voice on BECAUSE he cannot look at
  // the screen.
  if (r.technicalChunks?.length) {
    return buildSpokenTechnical(r.technicalChunks, lang);
  }

  // Rule 8b/8c (low-confidence), Rule 9 (vague), Rule 10 (too many),
  // not_found, redirected — the formatting call always supplies r.message.
  return spokenMessage(r.message);
}

// How many short values to read in a row. Two fuses protect the ABS unit and
// both are correct answers, so reading only the first would hide one; reading
// six bulb types would be a monologue.
const SPOKEN_FACT_LIMIT = 3;

// Same rule as everything above: reference, label, value and unit come from
// the database untouched, only the joining words are ours.
function speakFact(c: TechnicalChunk, lang: string): string | null {
  const label = c.label || c.heading;
  if (!label) return null;
  const value = [c.value, c.unit].filter(Boolean).join(' ');
  if (!value) return null;
  return tTechFact(lang, c.reference ?? null, label, value);
}

// A procedure is never spoken from here - findProcedure hands it to the
// consent gate instead, so the mechanic decides before a minute of speech
// begins. This function only ever produces short answers.
function buildSpokenTechnical(chunks: TechnicalChunk[], lang: string): string | null {
  const facts = chunks.filter(c => c.kind === 'fact');
  if (facts.length > 0) {
    const spoken = facts.slice(0, SPOKEN_FACT_LIMIT)
      .map(c => speakFact(c, lang))
      .filter((s): s is string => s !== null);
    if (spoken.length > 0) {
      const remaining = chunks.length - spoken.length;
      if (remaining > 0) spoken.push(tTechMoreResults(lang, remaining));
      return spoken.join(' ');
    }
  }

  // A drawing cannot be read aloud. Name it and say where it is - honest
  // about the limit, and he knows what to look at.
  const schema = chunks.find(c => c.kind === 'legend');
  if (schema) {
    const title = schema.heading || schema.documentTitle;
    if (title) return tTechSchema(lang, title);
  }

  return null;
}

// The procedure a technical answer offers to read, if any. Separate from
// buildSpokenText because it is not spoken immediately - it goes through the
// consent gate.
export function findProcedure(r: ChatResponse): TechnicalChunk | null {
  return r.technicalChunks?.find(c => c.kind === 'section' && !!c.body) ?? null;
}

// §5.6 Rule 8 consent detection — strict "yes"-equivalent match.
// Fail-safe: only affirmative words at the start of the transcript reveal
// the stored document. Anything else (negative, ambiguous, new fault code)
// routes as a new request and discards the pending consent.
// Tolerant of trailing words ("sì grazie", "yes please").
function isAffirmativeConsent(transcript: string, lang: string): boolean {
  const normalised = transcript.toLowerCase().trim().replace(/[.,!?¿¡]/g, '').trim();
  // Per-language primary affirmatives
  const byLang: Record<string, string[]> = {
    it: ['sì', 'si', 'certo', 'ok', 'va bene'],
    en: ['yes', 'yeah', 'yep', 'sure', 'ok'],
    fr: ['oui', 'ok', 'bien sur'],
    pt: ['sim', 'ok', 'claro'],
    es: ['sí', 'si', 'ok', 'claro'],
  };
  // Cross-language universal set always accepted regardless of detected lang
  const universal = ['yes', 'sì', 'si', 'oui', 'sim', 'sí', 'ok'];
  const candidates = new Set([...(byLang[lang] ?? byLang['it']), ...universal]);
  for (const a of candidates) {
    if (normalised === a || normalised.startsWith(a + ' ')) return true;
  }
  return false;
}

@Injectable({ providedIn: 'root' })
export class VoiceModeService {
  private readonly silenceDetector = new SilenceDetector();

  readonly state = signal<VoiceState>('idle');
  readonly engine = signal<VoiceEngine | null>(null);
  readonly toastMessage = signal<string | null>(null);
  readonly isActive = computed(() => this.engine() !== null);
  // Set when a transcript is ready but §4.1 routing hasn't fired yet.
  // ChatInputComponent watches this signal, runs the typing animation, then
  // calls commitPendingTranscript() — routing fires only after that.
  readonly pendingTranscript = signal<string | null>(null);

  // §5.6 Rule 8: full ChatResponse stored after a foundViaSharedEngine turn.
  // commitPendingTranscript checks this before routing the next transcript.
  // Cleared on consent (affirmative → speakStoredConsent), on discard
  // (negative/ambiguous → route as new request), and on stopVoiceMode().
  private pendingSharedEngineConsent: ChatResponse | null = null;

  // The procedure offered but not yet read. Same shape as the gate above,
  // deliberately a second field rather than a shared one: the shared-engine
  // gate is a safety contract (never dictate another brand's procedure
  // unasked) and this one is a courtesy (do not start a minute of speech
  // unasked). Merging them would let a change to the convenience quietly
  // weaken the contract.
  private pendingProcedureConsent: TechnicalChunk | null = null;

  // Forwards the SilenceDetector's existing AnalyserNode so ChatInputComponent
  // can drive the visualizer without creating a second AudioContext consumer.
  get analyserNode(): AnalyserNode | null { return this.silenceDetector.analyserNode; }

  private mediaStream: MediaStream | undefined;
  private mediaRecorder: MediaRecorder | undefined;
  private audioChunks: Blob[] = [];
  private noSpeechStop = false;
  private consecutiveFailures = 0;
  private consecutiveEmptyAttempts = 0;

  constructor(
    private readonly chatStore: ChatStore,
    private readonly speech: SpeechService,
    private readonly api: ChatApiService,
    private readonly voiceCarSelection: VoiceCarSelectionService,
  ) {
    // React to isStreaming going false when we're in waiting_response.
    // untracked() on state read prevents the effect from re-running on state
    // changes — we only want to react to isStreaming transitions.
    effect(() => {
      const streaming = this.chatStore.isStreaming();
      if (!streaming) {
        const currentState = untracked(() => this.state());
        if (currentState === 'waiting_response') {
          this.handleResponseReady();
        }
      }
    });
  }

  isSupported(type: VoiceEngine): boolean {
    return this.speech.isSupported(type);
  }

  startVoiceMode(type: VoiceEngine): void {
    if (!this.speech.isSupported(type)) return;
    if (this.isActive()) this.stopVoiceMode();
    this.engine.set(type);
    this.consecutiveFailures = 0;
    this.consecutiveEmptyAttempts = 0;
    this.state.set('listening');
    void this.startRecording();
  }

  stopVoiceMode(): void {
    this.speech.stop();
    this.silenceDetector.stop();
    this.mediaRecorder?.stop();
    this.mediaStream?.getTracks().forEach(t => t.stop());
    this.mediaStream = undefined;
    this.mediaRecorder = undefined;
    this.pendingTranscript.set(null);
    this.pendingSharedEngineConsent = null;
    this.pendingProcedureConsent = null;
    this.engine.set(null);
    this.state.set('idle');
  }

  // Called by ChatInputComponent after the typing animation completes.
  // Fires §4.1 routing and advances state to waiting_response.
  // Guard on engine() so a stop mid-animation is a safe no-op.
  commitPendingTranscript(): void {
    const transcript = this.pendingTranscript();
    this.pendingTranscript.set(null);
    if (transcript === null || this.engine() === null) return;

    // §5.6 Rule 8 consent gate — mirrors the §4.1 car-selection interception.
    // If a shared-engine disclosure was just spoken and we're waiting for the
    // mechanic's consent, intercept the next transcript here before it reaches
    // the backend. Affirmative → reveal stored causa/intervento locally, no
    // backend call. Anything else → discard consent, route as a new request.
    if (this.pendingSharedEngineConsent !== null) {
      const stored = this.pendingSharedEngineConsent;
      this.pendingSharedEngineConsent = null;
      if (isAffirmativeConsent(transcript, this.detLang())) {
        this.speakStoredConsent(stored);
        return; // no backend call, state goes directly to speaking → listening
      }
      // Negative or ambiguous: fall through and route transcript as new request.
    }

    // Procedure consent gate. Checked AFTER the shared-engine one on purpose:
    // that is a safety contract, this is a convenience, and only one of the
    // two can be pending at a time anyway.
    if (this.pendingProcedureConsent !== null) {
      const procedure = this.pendingProcedureConsent;
      this.pendingProcedureConsent = null;
      if (isAffirmativeConsent(transcript, this.detLang())) {
        this.speakProcedure(procedure);
        return; // no backend call - the text is already in hand
      }
      // Anything else: treat it as a new question, not as a refusal to answer.
    }

    const locallyHandled = this.routeTranscript(transcript);
    if (!locallyHandled) {
      this.state.set('waiting_response');
    }
  }

  private async startRecording(): Promise<void> {
    let stream: MediaStream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch {
      // §8 row 1: mic permission denied or device unavailable
      this.showToast(t(this.detLang(), 'mic_unavailable_toast'));
      this.engine.set(null);
      this.state.set('idle');
      return;
    }

    if (this.engine() === null) {
      // stopVoiceMode() was called while awaiting getUserMedia
      stream.getTracks().forEach(t => t.stop());
      return;
    }

    this.mediaStream = stream;
    this.audioChunks = [];
    this.noSpeechStop = false;
    this.mediaRecorder = new MediaRecorder(stream);

    this.mediaRecorder.ondataavailable = (e) => {
      if (e.data.size > 0) this.audioChunks.push(e.data);
    };
    this.mediaRecorder.onstop = () => void this.onRecordingStop();

    this.silenceDetector.start(
      stream,
      () => this.mediaRecorder?.stop(),      // onSilence: post-speech silence → stop
      () => {                                  // onNoSpeech: 8s with no amplitude
        this.noSpeechStop = true;
        this.mediaRecorder?.stop();
      },
    );

    this.mediaRecorder.start();
  }

  private async onRecordingStop(): Promise<void> {
    this.silenceDetector.stop();
    this.mediaStream?.getTracks().forEach(t => t.stop());
    this.mediaStream = undefined;

    if (this.state() === 'idle') return; // stopVoiceMode() was called mid-recording

    if (this.noSpeechStop) {
      this.noSpeechStop = false;
      this.onNothingHeard();
      return;
    }

    this.state.set('transcribing');

    const blob = new Blob(this.audioChunks, { type: 'audio/webm' });
    let transcript: string;
    try {
      transcript = await this.api.transcribe(blob);
    } catch {
      // §8 row 2: transcription API error
      this.consecutiveFailures++;
      this.showToast(t(this.detLang(), 'transcribe_failed_toast'));
      if (this.consecutiveFailures >= 2) {
        this.stopVoiceMode();
      } else {
        this.transitionToListening();
      }
      return;
    }

    this.consecutiveFailures = 0;

    if (!transcript.trim()) {
      this.onNothingHeard();
      return;
    }

    this.consecutiveEmptyAttempts = 0;

    // §4.1 routing is deferred: set pendingTranscript and stay in 'transcribing'
    // until ChatInputComponent finishes the typing animation and calls
    // commitPendingTranscript(). State stays 'transcribing' (amber processing
    // indicator) during the animation, then advances to 'waiting_response'.
    this.pendingTranscript.set(transcript.trim());
  }

  // §4.1: NEVER unconditionally send transcript as plain text.
  // Returns true when the transcript was handled locally (no backend call);
  // returns false when a backend call was dispatched (commitPendingTranscript
  // must then advance to waiting_response so the effect knows to speak the reply).
  //
  // Priority: car selection → doc selection → sendMessage (backend).
  // Car and doc selection are mutually exclusive in practice (the backend
  // returns either carMatches OR cases, not both), but car always wins.
  private routeTranscript(transcript: string): boolean {
    // Car selection (§4.1 / §5.4)
    const last = this.chatStore.lastResponse();
    if (last && last.carMatches.length > 0) {
      const displayOrder = this.chatStore.carDisplayOrder();
      const index = this.voiceCarSelection.parse(transcript, this.detLang(), displayOrder.length);
      if (index !== null) {
        const car = displayOrder[index];
        if (car) {
          void this.chatStore.confirmCar(car);
          return false; // confirmCar triggers a backend call
        }
      }
    }

    // Doc selection (§5.2) — strict=false for voice (speaker just says "due")
    const pendingDocs = this.chatStore.pendingDocSelection();
    if (pendingDocs) {
      const index = this.voiceCarSelection.parse(transcript, this.detLang(), pendingDocs.length);
      if (index !== null) {
        const caseSummary = pendingDocs[index];
        if (caseSummary) {
          this.chatStore.selectDocumentInLastResponse(index);
          this.speakCaseSummary(caseSummary);
          return true; // locally handled, no backend call
        }
      }
    }

    void this.chatStore.sendMessage(transcript);
    return false;
  }

  private handleResponseReady(): void {
    const response = this.chatStore.lastResponse();
    if (!response || this.engine() === null) return;

    // §5.6 Rule 8 cross-brand consent gate.
    // The backend always includes full causa/intervento even on the disclosure
    // turn (correct for non-voice UI — shows disclosure + card together).
    // For voice: store the full response and speak ONLY r.message (disclosure).
    // The next transcript goes through the consent gate in commitPendingTranscript
    // instead of being routed to the backend.
    if (response.cases?.[0]?.foundViaSharedEngine === true) {
      this.pendingSharedEngineConsent = response;
      const disclosure = spokenMessage(response.message);
      if (!disclosure) {
        // No disclosure text — stay in consent-pending, listen for "sì".
        this.transitionToListening();
        return;
      }
      this.state.set('speaking');
      this.speech.speak(disclosure, this.detLang(), this.engine() ?? 'web')
        .then(() => this.transitionToListening())
        .catch(() => {
          const key = this.engine() === 'google' ? 'hd_voice_unavailable_toast' : 'voice_unavailable_toast';
          this.showToast(t(this.detLang(), key));
          this.stopVoiceMode();
        });
      return;
    }

    // A procedure is offered, not read. Eight steps is about a minute of
    // speech, and starting it unasked is exactly what makes a hands-free
    // assistant tiring. Short values and diagrams get no such question: it
    // would cost more than the answer.
    const procedure = findProcedure(response);
    if (procedure) {
      this.pendingProcedureConsent = procedure;
      const title = procedure.heading || procedure.documentTitle || '';
      this.state.set('speaking');
      this.speech.speak(tTechOfferProcedure(this.detLang(), title), this.detLang(), this.engine() ?? 'web')
        .then(() => this.transitionToListening())
        .catch(() => {
          const key = this.engine() === 'google' ? 'hd_voice_unavailable_toast' : 'voice_unavailable_toast';
          this.showToast(t(this.detLang(), key));
          this.stopVoiceMode();
        });
      return;
    }

    const spokenText = buildSpokenText(response, this.detLang());
    if (!spokenText) {
      this.transitionToListening();
      return;
    }

    this.state.set('speaking');
    this.speech.speak(spokenText, this.detLang(), this.engine() ?? 'web')
      .then(() => this.transitionToListening())
      .catch(() => {
        // §8 row 5 (web) / row 6 (google): speech engine rejected
        const key = this.engine() === 'google'
          ? 'hd_voice_unavailable_toast'
          : 'voice_unavailable_toast';
        this.showToast(t(this.detLang(), key));
        this.stopVoiceMode();
      });
  }

  // Speaks causa+intervento from a stored shared-engine response without a
  // backend call. Called by commitPendingTranscript when the mechanic consents.
  // Reads a procedure the mechanic just accepted. No backend call - the text
  // was already in the response that offered it.
  //
  // stripMarkdown does the work that matters here: the body carries the
  // bullets added to lay the steps out on screen, and a synthesiser reads
  // "•" aloud or stumbles on it. The line breaks survive, and they are
  // what makes it pause between steps.
  private speakProcedure(procedure: TechnicalChunk): void {
    const text = stripMarkdown(procedure.body ?? '').trim();
    if (!text) {
      this.transitionToListening();
      return;
    }
    this.state.set('speaking');
    this.speech.speak(text, this.detLang(), this.engine() ?? 'web')
      .then(() => this.transitionToListening())
      .catch(() => {
        const key = this.engine() === 'google' ? 'hd_voice_unavailable_toast' : 'voice_unavailable_toast';
        this.showToast(t(this.detLang(), key));
        this.stopVoiceMode();
      });
  }

  private speakStoredConsent(stored: ChatResponse): void {
    const c = stored.cases[0];
    const parts: string[] = [];
    if (c?.causa)      parts.push(`${t(this.detLang(), 'causa_prefix')} ${c.causa}`);
    if (c?.intervento) parts.push(`${t(this.detLang(), 'intervento_prefix')} ${c.intervento}`);
    const text = parts.join(' ');
    if (!text) {
      this.transitionToListening();
      return;
    }
    this.state.set('speaking');
    this.speech.speak(text, this.detLang(), this.engine() ?? 'web')
      .then(() => this.transitionToListening())
      .catch(() => {
        const key = this.engine() === 'google' ? 'hd_voice_unavailable_toast' : 'voice_unavailable_toast';
        this.showToast(t(this.detLang(), key));
        this.stopVoiceMode();
      });
  }

  // Reads causa+intervento for a selected case without a backend call.
  // Called by routeTranscript when the user picks a doc by voice (§5.2).
  private speakCaseSummary(c: CaseSummary): void {
    const lang = this.detLang();
    const parts: string[] = [];
    if (c.causa)      parts.push(`${t(lang, 'causa_prefix')} ${c.causa}`);
    if (c.intervento) parts.push(`${t(lang, 'intervento_prefix')} ${c.intervento}`);
    const text = parts.join(' ');
    if (!text) {
      this.transitionToListening();
      return;
    }
    this.state.set('speaking');
    this.speech.speak(text, lang, this.engine() ?? 'web')
      .then(() => this.transitionToListening())
      .catch(() => {
        const key = this.engine() === 'google' ? 'hd_voice_unavailable_toast' : 'voice_unavailable_toast';
        this.showToast(t(lang, key));
        this.stopVoiceMode();
      });
  }

  private onNothingHeard(): void {
    // §8 rows 8–9: hint once, then exit on second consecutive empty
    this.consecutiveEmptyAttempts++;
    if (this.consecutiveEmptyAttempts >= 2) {
      this.stopVoiceMode();
      return;
    }
    this.state.set('speaking');
    this.speech.speak(t(this.detLang(), 'no_speech_hint'), this.detLang(), this.engine() ?? 'web')
      .then(() => this.transitionToListening())
      .catch(() => this.transitionToListening());
  }

  private transitionToListening(): void {
    if (this.engine() === null) return; // stopVoiceMode() called while speaking
    this.state.set('listening');
    void this.startRecording();
  }

  private detLang(): string {
    return this.chatStore.detectedLanguage();
  }

  private showToast(message: string): void {
    this.toastMessage.set(message);
    setTimeout(() => this.toastMessage.set(null), 4_000);
  }
}
