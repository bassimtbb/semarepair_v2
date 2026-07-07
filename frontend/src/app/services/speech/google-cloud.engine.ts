import type { ISpeechEngine } from './speech-engine.interface';

// Phase 2 stub — Google Cloud TTS is not implemented yet.
// isSupported() returns false so SpeechService never selects this engine
// during Phase 1. The ✨ button stays disabled with a "coming soon" tooltip.
export class GoogleCloudEngine implements ISpeechEngine {
  isSupported(): boolean { return false; }
  speak(_text: string, _lang: string): Promise<void> {
    return Promise.reject(new Error('Google Cloud TTS not yet implemented'));
  }
  stop(): void {}
}
