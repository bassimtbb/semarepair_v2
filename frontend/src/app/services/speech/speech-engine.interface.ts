export interface ISpeechEngine {
  speak(text: string, lang: string): Promise<void>;
  stop(): void;
  isSupported(): boolean;
  // Optional: called once from a genuine user-gesture click handler to unlock
  // iOS Safari's audio context before the first programmatic .play() call.
  init?(): void;
}
