import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideAudioLines, LucideSquare, LucideSend } from '@lucide/angular';
import { ChatApiService } from '../../../services/chat-api.service';

@Component({
  selector: 'app-chat-input',
  standalone: true,
  imports: [FormsModule, LucideAudioLines, LucideSquare, LucideSend],
  template: `
    <div class="input-bar-wrapper">
      <div class="input-bar bg-surface border-border">
        <button
          type="button"
          class="mic-button border-border text-foreground hover:bg-foreground/8"
          [class.recording]="isRecording()"
          [disabled]="disabled || isTranscribing()"
          (click)="toggleRecording()"
          [title]="isRecording() ? 'Interrompi registrazione' : 'Registra messaggio vocale'"
        >
          @if (isRecording()) {
            <svg lucideSquare [size]="18"></svg>
          } @else {
            <svg lucideAudioLines [size]="18"></svg>
          }
        </button>

        <input
          type="text"
          class="text-foreground"
          [(ngModel)]="text"
          [disabled]="disabled"
          placeholder="Descrivi il problema o inserisci un codice guasto..."
          (keydown.enter)="submit()"
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

  constructor(private readonly api: ChatApiService) {}

  submit(): void {
    if (!this.text.trim() || this.disabled) return;
    this.send.emit(this.text);
    this.text = '';
  }

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
