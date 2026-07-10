import { Injectable, computed, effect, signal, untracked } from '@angular/core';
import { ChatApiService } from './chat-api.service';
import { ChatStore } from './chat-store.service';
import { SilenceDetector } from './silence-detector';
import { SpeechService } from './speech/speech.service';
import { VoiceCarSelectionService } from './voice-car-selection.service';
import { t, tCarOption, tCarSelectionPrefix, tFoundNCases } from './voice-strings';
import type { ChatResponse } from '../models/chat.models';

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
export function buildSpokenText(r: ChatResponse, lang: string): string | null {
  // §5.6 Rule 8 safety net: if a shared-engine response somehow reaches here,
  // speak only the disclosure. The real gate is in handleResponseReady().
  if (r.cases?.[0]?.foundViaSharedEngine === true) {
    return r.message ?? null;
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
    const c = r.cases[0];
    const parts: string[] = [];
    if (r.cases.length >= 2) {
      // §5.2: multi-case announcement, then first case only
      parts.push(tFoundNCases(lang, r.cases.length));
    }
    if (c.causa)      parts.push(`${t(lang, 'causa_prefix')} ${c.causa}`);
    if (c.intervento) parts.push(`${t(lang, 'intervento_prefix')} ${c.intervento}`);
    if (r.cases.length >= 2) {
      parts.push(t(lang, 'see_screen'));
    }
    return parts.length > 0 ? parts.join(' ') : null;
  }

  // Rule 8b/8c (low-confidence), Rule 9 (vague), Rule 10 (too many),
  // not_found, redirected — the formatting call always supplies r.message.
  return r.message ?? null;
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

    this.routeTranscript(transcript);
    this.state.set('waiting_response');
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
  // If any car-selection list is pending (any size — the old 1–5 cap is
  // removed; §5.4 still limits what is READ aloud, but recognition is
  // unlimited), try to parse the transcript as a number first.
  // A match → confirmCar() on the car at that visual-order position.
  // "due" as plain text would trigger TooVague validation and fail.
  // Only fall through to sendMessage() if parsing finds no number.
  private routeTranscript(transcript: string): void {
    const last = this.chatStore.lastResponse();
    if (last && last.carMatches.length > 0) {
      const displayOrder = this.chatStore.carDisplayOrder();
      const index = this.voiceCarSelection.parse(transcript, this.detLang(), displayOrder.length);
      if (index !== null) {
        const car = displayOrder[index];
        if (car) {
          void this.chatStore.confirmCar(car);
          return;
        }
      }
    }
    void this.chatStore.sendMessage(transcript);
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
      const disclosure = response.message ?? null;
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
