// Uses Web Audio API AnalyserNode on the same MediaStream as the MediaRecorder.
// Callbacks fire on the Angular zone via the caller (VoiceModeService runs in
// a service context where Angular tracks all async events).

const RMS_THRESHOLD = 0.01;         // below this = silence
const SILENCE_GRACE_MS = 1_500;     // must be silent for this long before stop
const SILENCE_STOP_MS = 2_000;      // silence window that triggers onstop
const NO_SPEECH_TIMEOUT_MS = 8_000; // no speech at all → hint
const HARD_CAP_MS = 30_000;         // maximum recording length

export class SilenceDetector {
  private ctx: AudioContext | null = null;
  private analyser: AnalyserNode | null = null;
  private source: MediaStreamAudioSourceNode | null = null;
  private rafHandle: number | null = null;
  private silenceStart: number | null = null;
  private speechDetected = false;
  private noSpeechTimer: ReturnType<typeof setTimeout> | null = null;
  private hardCapTimer: ReturnType<typeof setTimeout> | null = null;

  start(
    stream: MediaStream,
    onSilence: () => void,
    onNoSpeech: () => void,
  ): void {
    this.stop();

    this.ctx = new AudioContext();
    this.analyser = this.ctx.createAnalyser();
    this.analyser.fftSize = 256;
    this.source = this.ctx.createMediaStreamSource(stream);
    this.source.connect(this.analyser);

    const buffer = new Float32Array(this.analyser.fftSize);
    const startTime = Date.now();
    this.silenceStart = null;
    this.speechDetected = false;

    this.noSpeechTimer = setTimeout(() => {
      if (!this.speechDetected) onNoSpeech();
    }, NO_SPEECH_TIMEOUT_MS);

    this.hardCapTimer = setTimeout(() => {
      onSilence();
    }, HARD_CAP_MS);

    const tick = (): void => {
      if (!this.analyser) return;
      this.analyser.getFloatTimeDomainData(buffer);

      let sumSq = 0;
      for (let i = 0; i < buffer.length; i++) sumSq += buffer[i] * buffer[i];
      const rms = Math.sqrt(sumSq / buffer.length);

      const isSilent = rms < RMS_THRESHOLD;
      const now = Date.now();

      if (!isSilent) {
        this.speechDetected = true;
        this.silenceStart = null;
      } else {
        if (this.speechDetected) {
          if (this.silenceStart === null) {
            this.silenceStart = now;
          } else if (now - this.silenceStart >= SILENCE_STOP_MS) {
            this.stop();
            onSilence();
            return;
          }
        }
      }

      this.rafHandle = requestAnimationFrame(tick);
    };

    this.rafHandle = requestAnimationFrame(tick);
  }

  stop(): void {
    if (this.rafHandle !== null) {
      cancelAnimationFrame(this.rafHandle);
      this.rafHandle = null;
    }
    if (this.noSpeechTimer !== null) {
      clearTimeout(this.noSpeechTimer);
      this.noSpeechTimer = null;
    }
    if (this.hardCapTimer !== null) {
      clearTimeout(this.hardCapTimer);
      this.hardCapTimer = null;
    }
    this.source?.disconnect();
    this.ctx?.close().catch(() => {});
    this.source = null;
    this.analyser = null;
    this.ctx = null;
  }
}
