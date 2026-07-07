import type { ISpeechEngine } from './speech-engine.interface';

// iOS Safari requires the audio context to be unlocked via a user gesture
// before programmatic .play() calls work in async callbacks (e.g. when TTS
// audio arrives after a voice-pipeline response). init() plays a silent WAV
// through the SAME <audio> element to satisfy this requirement. It must be
// called directly from a click handler, not from async code or effects.
function createSilentWavBlob(): Blob {
  // Minimal valid WAV: 46 bytes = 44-byte header + 2 bytes PCM silence.
  // 16-bit mono, 22050 Hz, 1 sample.
  const buf = new ArrayBuffer(46);
  const v = new DataView(buf);
  v.setUint32(0,  0x52494646, false); // "RIFF"
  v.setUint32(4,  38,         true);  // file size - 8
  v.setUint32(8,  0x57415645, false); // "WAVE"
  v.setUint32(12, 0x666d7420, false); // "fmt "
  v.setUint32(16, 16,         true);  // subchunk1 size
  v.setUint16(20, 1,          true);  // PCM format
  v.setUint16(22, 1,          true);  // 1 channel
  v.setUint32(24, 22050,      true);  // sample rate
  v.setUint32(28, 44100,      true);  // byte rate (22050 * 1 * 2)
  v.setUint16(32, 2,          true);  // block align
  v.setUint16(34, 16,         true);  // bits per sample
  v.setUint32(36, 0x64617461, false); // "data"
  v.setUint32(40, 2,          true);  // data size (1 sample = 2 bytes)
  v.setInt16(44,  0,          true);  // 1 sample of silence
  return new Blob([buf], { type: 'audio/wav' });
}

export class GoogleCloudEngine implements ISpeechEngine {
  private readonly audio = new Audio();
  private currentBlobUrl: string | null = null;
  private initDone = false;

  isSupported(): boolean {
    // Google Cloud TTS is available on all platforms (no OS voice packs needed).
    // The backend may return 503 tts_not_configured if the API key is absent —
    // that's handled at speak() time, not here.
    return true;
  }

  // Must be called from a genuine click handler (user gesture) to unlock
  // iOS Safari audio before the first programmatic .play() fires.
  init(): void {
    if (this.initDone) return;
    this.initDone = true;
    const silentUrl = URL.createObjectURL(createSilentWavBlob());
    this.audio.src = silentUrl;
    this.audio.volume = 0;
    this.audio.play()
      .then(() => URL.revokeObjectURL(silentUrl))
      .catch(() => URL.revokeObjectURL(silentUrl));
  }

  speak(text: string, lang: string): Promise<void> {
    return new Promise((resolve, reject) => {
      // Revoke any previous blob URL before replacing it.
      if (this.currentBlobUrl) {
        URL.revokeObjectURL(this.currentBlobUrl);
        this.currentBlobUrl = null;
      }

      fetch('/api/chat/tts', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text, language: lang }),
      })
        .then(resp => {
          if (!resp.ok) {
            // Parse the JSON error body so the rejection message is informative.
            return resp.json()
              .then(
                (err: { error?: string }) =>
                  Promise.reject(new Error(err?.error ?? 'http_error')),
                () => Promise.reject(new Error('http_error')),
              );
          }
          return resp.blob();
        })
        .then(blob => {
          const url = URL.createObjectURL(blob);
          this.currentBlobUrl = url;
          this.audio.src = url;
          this.audio.volume = 1;

          this.audio.onended = () => {
            URL.revokeObjectURL(url);
            if (this.currentBlobUrl === url) this.currentBlobUrl = null;
            resolve();
          };
          this.audio.onerror = () => {
            URL.revokeObjectURL(url);
            if (this.currentBlobUrl === url) this.currentBlobUrl = null;
            reject(new Error('audio_playback_error'));
          };

          return this.audio.play();
        })
        .catch(err => reject(err instanceof Error ? err : new Error(String(err))));
    });
  }

  stop(): void {
    this.audio.pause();
    if (this.currentBlobUrl) {
      URL.revokeObjectURL(this.currentBlobUrl);
      this.currentBlobUrl = null;
    }
    this.audio.src = '';
    // Reset event handlers so a stopped speak() doesn't fire stale resolve/reject.
    this.audio.onended = null;
    this.audio.onerror = null;
  }
}
