import { Injectable } from '@angular/core';
import { parseCarSelection } from './selection-parser';

// Thin injectable wrapper around the shared parseCarSelection utility.
// Keeping this as a service preserves the injection point so VoiceModeService
// and any future callers don't need to be refactored away from DI.
@Injectable({ providedIn: 'root' })
export class VoiceCarSelectionService {
  // Returns the 0-based visual-order index of the selected car, or null
  // if no number reference is found, the reference is ambiguous, or the
  // number exceeds listLength. Delegates entirely to parseCarSelection —
  // the 1–5 cap from the old implementation is removed.
  parse(transcript: string, lang: string, listLength: number): number | null {
    return parseCarSelection(transcript, lang, listLength);
  }
}
