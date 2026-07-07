import type { ISpeechEngine } from './speech-engine.interface';

// Android Chrome silently cuts TTS at ~250 chars mid-sentence. Fix: split into
// ≤180-char chunks at sentence boundaries, chain via utterance.onend, and call
// speechSynthesis.resume() every 14s (another Android bug: synthesis pauses
// without notification after a few seconds of audio).
const ANDROID_CHUNK_MAX = 180;
const ANDROID_RESUME_INTERVAL_MS = 14_000;

function splitIntoChunks(text: string): string[] {
  if (text.length <= ANDROID_CHUNK_MAX) return [text];
  const chunks: string[] = [];
  // Split on sentence-ending punctuation followed by whitespace.
  const parts = text.split(/(?<=[.!?;])\s+/);
  let current = '';
  for (const part of parts) {
    if (!current) {
      current = part;
    } else if (current.length + 1 + part.length <= ANDROID_CHUNK_MAX) {
      current += ' ' + part;
    } else {
      chunks.push(current);
      current = part;
    }
  }
  if (current) chunks.push(current);
  return chunks;
}

// iOS Safari blocks speechSynthesis.speak() outside a direct user-gesture
// call stack (programmatic invocations from async callbacks are silently
// dropped). isSupported() returns false on iOS so VoiceModeService falls
// back to showing a tooltip instead of attempting to speak.
function isIOS(): boolean {
  return /iPad|iPhone|iPod/.test(navigator.userAgent) && !(window as Window & { MSStream?: unknown }).MSStream;
}

export class WebSpeechEngine implements ISpeechEngine {
  private currentUtterance: SpeechSynthesisUtterance | null = null;
  private resumeTimer: ReturnType<typeof setInterval> | null = null;
  private stopped = false;

  isSupported(): boolean {
    return !isIOS() && typeof speechSynthesis !== 'undefined';
  }

  stop(): void {
    this.stopped = true;
    this.clearResumeTimer();
    if (typeof speechSynthesis !== 'undefined') {
      speechSynthesis.cancel();
    }
    this.currentUtterance = null;
  }

  speak(text: string, lang: string): Promise<void> {
    return new Promise((resolve, reject) => {
      if (!this.isSupported()) {
        reject(new Error('WebSpeech not supported'));
        return;
      }

      this.stopped = false;
      speechSynthesis.cancel();

      const chunks = splitIntoChunks(text);
      let index = 0;

      const speakNext = (): void => {
        if (this.stopped || index >= chunks.length) {
          this.clearResumeTimer();
          if (!this.stopped) resolve();
          return;
        }

        const utterance = new SpeechSynthesisUtterance(chunks[index++]);
        utterance.lang = lang;
        this.currentUtterance = utterance;

        utterance.onend = () => {
          this.clearResumeTimer();
          speakNext();
        };

        utterance.onerror = (e) => {
          this.clearResumeTimer();
          if (!this.stopped) reject(new Error(e.error));
        };

        // Android Chrome pauses synthesis silently — nudge it every 14s.
        this.clearResumeTimer();
        this.resumeTimer = setInterval(() => {
          if (!this.stopped && speechSynthesis.paused) {
            speechSynthesis.resume();
          }
        }, ANDROID_RESUME_INTERVAL_MS);

        speechSynthesis.speak(utterance);
      };

      speakNext();
    });
  }

  private clearResumeTimer(): void {
    if (this.resumeTimer !== null) {
      clearInterval(this.resumeTimer);
      this.resumeTimer = null;
    }
  }
}
