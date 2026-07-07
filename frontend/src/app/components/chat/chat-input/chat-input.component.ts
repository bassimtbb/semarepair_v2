// Typing animation note: Gemini transcription delivers a complete result
// (single API call on the full audio blob — no interim partials). The
// character-by-character type-in below gives the PERCEPTION of live text.
//
// Alternative (not implemented): webkitSpeechRecognition with
// interimResults=true would give genuinely incremental display on Chrome,
// with Gemini remaining authoritative for the final commit. Documented here
// as a future option; not implemented because it's Chrome-only and adds a
// second STT consumer.
import { Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild, effect, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAudioLines, LucideSparkles, LucideSquare, LucideVolume2, LucideSend } from '@lucide/angular';
import { ChatApiService } from '../../../services/chat-api.service';
import { SpeechService } from '../../../services/speech/speech.service';
import { VoiceModeService } from '../../../services/voice-mode.service';
import type { VoiceEngine } from '../../../services/voice-mode.service';

@Component({
  selector: 'app-chat-input',
  standalone: true,
  imports: [FormsModule, LucideAudioLines, LucideSparkles, LucideSquare, LucideVolume2, LucideSend],
  template: `
    @if (voiceMode.toastMessage()) {
      <div class="voice-toast">{{ voiceMode.toastMessage() }}</div>
    }
    <div class="input-bar-wrapper">
      <div class="input-bar bg-surface border-border">

        <!-- 🎤 Mic: frozen per §6.6 -->
        <button
          type="button"
          class="mic-button border-border text-foreground hover:bg-foreground/8"
          [class.recording]="isRecording()"
          [disabled]="disabled || isTranscribing() || voiceMode.isActive()"
          (click)="toggleRecording()"
          [title]="isRecording() ? 'Interrompi registrazione' : 'Registra messaggio vocale'"
        >
          @if (isRecording()) {
            <svg lucideSquare [size]="18"></svg>
          } @else {
            <svg lucideAudioLines [size]="18"></svg>
          }
        </button>

        <!-- 🔊 Voice Orb (Web Speech API) -->
        <button
          type="button"
          class="voice-orb"
          [class.voice-orb--listening]="voiceMode.engine() === 'web' && voiceMode.state() === 'listening'"
          [class.voice-orb--processing]="voiceMode.engine() === 'web' && (voiceMode.state() === 'transcribing' || voiceMode.state() === 'waiting_response')"
          [class.voice-orb--speaking]="voiceMode.engine() === 'web' && voiceMode.state() === 'speaking'"
          [disabled]="disabled || (voiceMode.isActive() && voiceMode.engine() !== 'web') || !voiceMode.isSupported('web')"
          (click)="toggleVoiceMode('web')"
          [title]="voiceButtonTitle('web')"
        >
          <svg lucideVolume2 [size]="16" class="orb-icon"></svg>
        </button>

        <!-- ✨ Voice HD Orb (Google Cloud — Phase 2 stub, always disabled) -->
        <button
          type="button"
          class="voice-orb voice-orb--hd"
          [class.voice-orb--listening]="voiceMode.engine() === 'google' && voiceMode.state() === 'listening'"
          [class.voice-orb--processing]="voiceMode.engine() === 'google' && (voiceMode.state() === 'transcribing' || voiceMode.state() === 'waiting_response')"
          [class.voice-orb--speaking]="voiceMode.engine() === 'google' && voiceMode.state() === 'speaking'"
          [disabled]="disabled || (voiceMode.isActive() && voiceMode.engine() !== 'google') || !voiceMode.isSupported('google')"
          (click)="toggleVoiceMode('google')"
          [title]="voiceButtonTitle('google')"
        >
          <svg lucideSparkles [size]="16" class="orb-icon"></svg>
        </button>

        <!-- Input + visualizer: canvas always in DOM, opacity driven by CSS -->
        <div class="input-area">
          <input
            type="text"
            class="viz-input text-foreground"
            [class.viz-input--hidden]="voiceMode.state() === 'listening'"
            [ngModel]="textSig()"
            (ngModelChange)="textSig.set($event)"
            [disabled]="disabled"
            placeholder="Descrivi il problema o inserisci un codice guasto..."
            (keydown.enter)="submit()"
            (input)="onTextInput()"
          />
          <canvas
            #vizCanvas
            class="voice-viz"
            [class.voice-viz--active]="voiceMode.state() === 'listening'"
          ></canvas>
        </div>

        <button
          type="button"
          class="send-button bg-accent text-accent-foreground hover:bg-accent/90"
          [disabled]="disabled || !textSig().trim()"
          (click)="submit()"
        >
          <svg lucideSend [size]="16"></svg>
        </button>

      </div>
    </div>
  `,
  styleUrls: ['./chat-input.component.css'],
})
export class ChatInputComponent implements OnDestroy {
  @ViewChild('vizCanvas') private vizCanvas?: ElementRef<HTMLCanvasElement>;

  @Input() disabled = false;
  @Output() send = new EventEmitter<string>();

  readonly textSig = signal('');
  readonly isRecording = signal(false);
  readonly isTranscribing = signal(false);

  private mediaRecorder?: MediaRecorder;
  private audioChunks: Blob[] = [];
  private vizRaf: number | null = null;
  private animHandle: ReturnType<typeof setTimeout> | null = null;

  constructor(
    private readonly api: ChatApiService,
    readonly voiceMode: VoiceModeService,
    private readonly speech: SpeechService,
  ) {
    // Start typing animation whenever a transcript arrives from the service
    effect(() => {
      const transcript = this.voiceMode.pendingTranscript();
      if (transcript !== null) {
        untracked(() => this.startTypeAnimation(transcript));
      }
    });

    // Visualizer loop: run while voice mode is active
    effect(() => {
      const active = this.voiceMode.isActive();
      untracked(() => {
        if (active) this.startVizLoop();
        else this.stopVizLoop();
      });
    });
  }

  ngOnDestroy(): void {
    this.stopVizLoop();
    this.cancelTypeAnimation(false);
  }

  submit(): void {
    if (!this.textSig().trim() || this.disabled) return;
    this.send.emit(this.textSig());
    this.textSig.set('');
  }

  toggleVoiceMode(type: VoiceEngine): void {
    if (this.voiceMode.engine() === type) {
      this.voiceMode.stopVoiceMode();
    } else {
      // iOS audio unlock: must happen inside the click handler (user gesture)
      // before any async code runs, so .play() is allowed later in the pipeline.
      this.speech.initForGesture(type);
      this.voiceMode.startVoiceMode(type);
    }
  }

  voiceButtonTitle(type: VoiceEngine): string {
    if (!this.voiceMode.isSupported(type)) {
      return type === 'google' ? 'Voice HD (disponibile presto)' : 'Voce non supportata su questo browser';
    }
    if (this.voiceMode.engine() === type) return 'Interrompi voce';
    return type === 'google' ? 'Voice HD' : 'Avvia voce';
  }

  onTextInput(): void {
    // User interacted during animation → skip to full text so what they see = what gets sent
    if (this.animHandle !== null) {
      this.cancelTypeAnimation(true);
      return;
    }
    if (this.voiceMode.isActive()) this.voiceMode.stopVoiceMode();
  }

  // --- Typing animation ---

  private startTypeAnimation(transcript: string): void {
    this.cancelTypeAnimation(false);
    this.textSig.set('');
    let i = 0;

    const tick = (): void => {
      if (i >= transcript.length) {
        this.animHandle = null;
        this.textSig.set(transcript); // ensure exact final value
        // 80ms pause so user reads the full text before it sends + clears
        setTimeout(() => {
          this.voiceMode.commitPendingTranscript();
          setTimeout(() => this.textSig.set(''), 350);
        }, 80);
        return;
      }
      this.textSig.set(transcript.slice(0, ++i));
      this.animHandle = setTimeout(tick, 15);
    };

    this.animHandle = setTimeout(tick, 15);
  }

  private cancelTypeAnimation(skip: boolean): void {
    if (this.animHandle !== null) {
      clearTimeout(this.animHandle);
      this.animHandle = null;
    }
    if (skip) {
      const full = this.voiceMode.pendingTranscript();
      if (full !== null) {
        this.textSig.set(full);
        this.voiceMode.commitPendingTranscript();
        setTimeout(() => this.textSig.set(''), 350);
      }
    }
  }

  // --- Visualizer ---

  private startVizLoop(): void {
    if (this.vizRaf !== null) return;

    const draw = (): void => {
      if (!this.voiceMode.isActive()) {
        this.vizRaf = null;
        return;
      }

      const canvas = this.vizCanvas?.nativeElement;
      if (!canvas) {
        this.vizRaf = requestAnimationFrame(draw);
        return;
      }

      // Sync canvas buffer resolution with CSS layout dimensions (cheap no-op when unchanged)
      if (canvas.width !== canvas.offsetWidth) canvas.width = canvas.offsetWidth || 240;
      if (canvas.height !== canvas.offsetHeight) canvas.height = canvas.offsetHeight || 44;

      const W = canvas.width;
      const H = canvas.height;
      const ctx = canvas.getContext('2d');
      if (!ctx) { this.vizRaf = requestAnimationFrame(draw); return; }

      ctx.clearRect(0, 0, W, H);

      const analyser = this.voiceMode.analyserNode;
      let data: Uint8Array | null = null;
      if (analyser) {
        data = new Uint8Array(analyser.frequencyBinCount);
        analyser.getByteFrequencyData(data);
      }

      const isDark = document.documentElement.getAttribute('data-theme') === 'dark' ||
        (!document.documentElement.getAttribute('data-theme') &&
          window.matchMedia('(prefers-color-scheme: dark)').matches);

      const barCount = 28;
      const barW = 3;
      const gap = 2;
      const totalW = barCount * (barW + gap) - gap;
      const startX = Math.max(0, (W - totalW) / 2);

      for (let i = 0; i < barCount; i++) {
        const val = data ? data[Math.floor(i * data.length / barCount)] / 255 : 0;
        const minH = 3;
        const maxH = H - 8;
        const barH = Math.max(minH, minH + val * (maxH - minH));
        const x = startX + i * (barW + gap);
        const y = (H - barH) / 2;
        const alpha = 0.3 + val * 0.7;
        const rgb = isDark ? '147,197,253' : '37,99,235';

        ctx.fillStyle = `rgba(${rgb},${alpha})`;
        ctx.beginPath();
        ctx.roundRect(x, y, barW, barH, barW / 2);
        ctx.fill();
      }

      this.vizRaf = requestAnimationFrame(draw);
    };

    this.vizRaf = requestAnimationFrame(draw);
  }

  private stopVizLoop(): void {
    if (this.vizRaf !== null) {
      cancelAnimationFrame(this.vizRaf);
      this.vizRaf = null;
    }
  }

  // ---- Mic button — DO NOT MODIFY (frozen per §6.6) ----

  async toggleRecording(): Promise<void> {
    if (this.isRecording()) {
      this.mediaRecorder?.stop();
      return;
    }

    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    this.audioChunks = [];
    this.mediaRecorder = new MediaRecorder(stream);

    this.mediaRecorder.ondataavailable = (e) => this.audioChunks.push(e.data);
    this.mediaRecorder.onstop = async () => {
      stream.getTracks().forEach(track => track.stop());
      this.isRecording.set(false);
      this.isTranscribing.set(true);
      try {
        const blob = new Blob(this.audioChunks, { type: 'audio/webm' });
        const transcript = await this.api.transcribe(blob);
        this.textSig.set(transcript);
      } catch {
        // Non-fatal — mechanic can type instead.
      } finally {
        this.isTranscribing.set(false);
      }
    };

    this.mediaRecorder.start();
    this.isRecording.set(true);
  }
}
