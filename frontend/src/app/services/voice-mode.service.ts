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
//   1. §5.6 foundViaSharedEngine guard — MUST be first (ask-first contract)
//   2. Car selection (§5.4)
//   3. Found document (§5.1 single / §5.2 multi)
//   4. Everything else: speak r.message verbatim (Rule 8b/8c/9/10/not_found)
export function buildSpokenText(r: ChatResponse, lang: string): string | null {
  // §5.6 Rule 8 cross-brand ask-first guard. Must be FIRST per §5.8.
  //
  // WHY flag alone is insufficient: Search Service never resets
  // foundViaSharedEngine — it is a property of the search result, not
  // session state. After the mechanic confirms ("sì"), the LLM issues the
  // same search call again and gets foundViaSharedEngine=true a second time.
  // BuildFormatting still generates a non-null message on that second turn
  // (its rule fires on the flag, not on whether the mechanic has consented).
  // A flag-only guard loops the disclosure indefinitely.
  //
  // Fix: speak the disclosure only on Turn 1 (causa absent = document not
  // yet revealed). On Turn 2 the LLM has retrieved the actual document so
  // causa + intervento are populated — the guard is false and §5.1 below
  // reads them out normally.
  const sharedEngine = r.cases?.[0]?.foundViaSharedEngine === true;
  const hasRealDocument = !!r.cases?.[0]?.causa && !!r.cases?.[0]?.intervento;
  if (sharedEngine && !hasRealDocument) {
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

@Injectable({ providedIn: 'root' })
export class VoiceModeService {
  private readonly silenceDetector = new SilenceDetector();

  readonly state = signal<VoiceState>('idle');
  readonly engine = signal<VoiceEngine | null>(null);
  readonly toastMessage = signal<string | null>(null);
  readonly isActive = computed(() => this.engine() !== null);

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
    this.engine.set(null);
    this.state.set('idle');
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

    // §4.1 decision point: route transcript to confirm-car or send-message
    this.routeTranscript(transcript.trim());
    this.state.set('waiting_response');
  }

  // §4.1: NEVER unconditionally send transcript as plain text.
  // If the last response had 1–5 car options pending, try to parse the
  // transcript as a number first. A match → structured confirmCarByIndex()
  // payload (not text). "due" as plain text would trigger TooVague validation
  // and fail. Only fall through to sendMessage() if parsing finds no number.
  private routeTranscript(transcript: string): void {
    const last = this.chatStore.lastResponse();
    if (last && last.carMatches.length > 0 && last.carMatches.length <= 5) {
      const index = this.voiceCarSelection.parse(transcript, this.detLang(), last.carMatches.length);
      if (index !== null) {
        void this.chatStore.confirmCarByIndex(index);
        return;
      }
    }
    void this.chatStore.sendMessage(transcript);
  }

  private handleResponseReady(): void {
    const response = this.chatStore.lastResponse();
    if (!response || this.engine() === null) return;

    const spokenText = buildSpokenText(response, this.detLang());
    if (!spokenText) {
      this.transitionToListening();
      return;
    }

    this.state.set('speaking');
    this.speech.speak(spokenText, this.detLang(), this.engine() ?? 'web')
      .then(() => this.transitionToListening())
      .catch(() => {
        // §8 row 5: web speech synthesis failed (utterance.onerror)
        this.showToast(t(this.detLang(), 'voice_unavailable_toast'));
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
