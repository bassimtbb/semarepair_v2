import { Injectable } from '@angular/core';
import type { ISpeechEngine } from './speech-engine.interface';
import { WebSpeechEngine } from './web-speech.engine';
import { GoogleCloudEngine } from './google-cloud.engine';

@Injectable({ providedIn: 'root' })
export class SpeechService {
  private readonly engines: Record<'web' | 'google', ISpeechEngine> = {
    web: new WebSpeechEngine(),
    google: new GoogleCloudEngine(),
  };

  private active: ISpeechEngine | null = null;

  isSupported(type: 'web' | 'google'): boolean {
    return this.engines[type].isSupported();
  }

  // Called from a genuine click handler so iOS Safari's audio context is
  // unlocked before the first programmatic .play() fires later in the pipeline.
  initForGesture(type: 'web' | 'google'): void {
    this.engines[type].init?.();
  }

  speak(text: string, lang: string, engine: 'web' | 'google' = 'web'): Promise<void> {
    this.active = this.engines[engine];
    return this.active.speak(text, lang);
  }

  stop(): void {
    this.active?.stop();
    this.active = null;
  }
}
