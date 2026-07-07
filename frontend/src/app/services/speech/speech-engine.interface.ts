export interface ISpeechEngine {
  speak(text: string, lang: string): Promise<void>;
  stop(): void;
  isSupported(): boolean;
}
