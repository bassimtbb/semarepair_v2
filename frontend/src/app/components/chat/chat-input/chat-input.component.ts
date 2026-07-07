import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAudioLines, LucideSparkles, LucideSquare, LucideVolume2, LucideSend } from '@lucide/angular';
import { ChatApiService } from '../../../services/chat-api.service';
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

        <!-- 🎤 Mic: frozen logic, only [disabled] binding added for voice mode -->
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

        <!-- 🔊 Voice (Web Speech) -->
        <button
          type="button"
          class="voice-button border-border text-foreground"
          [class.voice-listening]="voiceMode.engine() === 'web' && voiceMode.state() === 'listening'"
          [class.voice-processing]="voiceMode.engine() === 'web' && (voiceMode.state() === 'transcribing' || voiceMode.state() === 'waiting_response')"
          [class.voice-speaking]="voiceMode.engine() === 'web' && voiceMode.state() === 'speaking'"
          [disabled]="disabled || (voiceMode.isActive() && voiceMode.engine() !== 'web') || !voiceMode.isSupported('web')"
          (click)="toggleVoiceMode('web')"
          [title]="voiceButtonTitle('web')"
        >
          <svg lucideVolume2 [size]="18"></svg>
        </button>

        <!-- ✨ Voice HD (Google Cloud — Phase 2 stub, always disabled) -->
        <button
          type="button"
          class="voice-button border-border text-foreground"
          [class.voice-listening]="voiceMode.engine() === 'google' && voiceMode.state() === 'listening'"
          [class.voice-processing]="voiceMode.engine() === 'google' && (voiceMode.state() === 'transcribing' || voiceMode.state() === 'waiting_response')"
          [class.voice-speaking]="voiceMode.engine() === 'google' && voiceMode.state() === 'speaking'"
          [disabled]="disabled || (voiceMode.isActive() && voiceMode.engine() !== 'google') || !voiceMode.isSupported('google')"
          (click)="toggleVoiceMode('google')"
          [title]="voiceButtonTitle('google')"
        >
          <svg lucideSparkles [size]="18"></svg>
        </button>

        <input
          type="text"
          class="text-foreground"
          [(ngModel)]="text"
          [disabled]="disabled"
          placeholder="Descrivi il problema o inserisci un codice guasto..."
          (keydown.enter)="submit()"
          (input)="onTextInput()"
        />

        <button
          type="button"
          class="send-button bg-accent text-accent-foreground hover:bg-accent/90"
          [disabled]="disabled || !text.trim()"
          (click)="submit()"
        >
          <svg lucideSend [size]="16"></svg>
        </button>
      </div>
    </div>
  `,
  styleUrls: ['./chat-input.component.css'],
})
export class ChatInputComponent {
  @Input() disabled = false;
  @Output() send = new EventEmitter<string>();

  text = '';
  readonly isRecording = signal(false);
  readonly isTranscribing = signal(false);

  private mediaRecorder?: MediaRecorder;
  private audioChunks: Blob[] = [];

  constructor(
    private readonly api: ChatApiService,
    readonly voiceMode: VoiceModeService,
  ) {}

  submit(): void {
    if (!this.text.trim() || this.disabled) return;
    this.send.emit(this.text);
    this.text = '';
  }

  toggleVoiceMode(type: VoiceEngine): void {
    if (this.voiceMode.engine() === type) {
      this.voiceMode.stopVoiceMode();
    } else {
      this.voiceMode.startVoiceMode(type);
    }
  }

  voiceButtonTitle(type: VoiceEngine): string {
    if (!this.voiceMode.isSupported(type)) return type === 'google' ? 'Voice HD (disponibile presto)' : 'Voce non supportata su questo browser';
    if (this.voiceMode.engine() === type) return 'Interrompi voce';
    return type === 'google' ? 'Voice HD' : 'Avvia voce';
  }

  onTextInput(): void {
    if (this.voiceMode.isActive()) this.voiceMode.stopVoiceMode();
  }

  // ---- Mic button logic: DO NOT MODIFY (frozen per spec §6.6) ----

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
        this.text = transcript;
      } catch {
        // Transcription failure is non-fatal - the mechanic can just type instead.
      } finally {
        this.isTranscribing.set(false);
      }
    };

    this.mediaRecorder.start();
    this.isRecording.set(true);
  }
}
