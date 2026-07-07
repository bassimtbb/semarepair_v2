import { Injectable } from '@angular/core';

// Maps spoken number words (and ordinals) in all 5 supported languages to
// 1-based indices. Returns null on ambiguity (two different numbers matched).
// Only indices 1–5 are recognized — car lists larger than 5 are never read
// aloud (VoiceModeService switches to a narrow-down prompt path for >5 results).

const NUMBER_MAPS: Record<string, Record<string, number>> = {
  it: {
    uno: 1, prima: 1, primo: 1, '1': 1,
    due: 2, seconda: 2, secondo: 2, '2': 2,
    tre: 3, terza: 3, terzo: 3, '3': 3,
    quattro: 4, quarta: 4, quarto: 4, '4': 4,
    cinque: 5, quinta: 5, quinto: 5, '5': 5,
  },
  en: {
    one: 1, first: 1, '1': 1,
    two: 2, second: 2, '2': 2,
    three: 3, third: 3, '3': 3,
    four: 4, fourth: 4, '4': 4,
    five: 5, fifth: 5, '5': 5,
  },
  fr: {
    un: 1, une: 1, premier: 1, première: 1, '1': 1,
    deux: 2, deuxième: 2, '2': 2,
    trois: 3, troisième: 3, '3': 3,
    quatre: 4, quatrième: 4, '4': 4,
    cinq: 5, cinquième: 5, '5': 5,
  },
  pt: {
    um: 1, uma: 1, primeiro: 1, primeira: 1, '1': 1,
    dois: 2, duas: 2, segundo: 2, segunda: 2, '2': 2,
    três: 3, terceiro: 3, terceira: 3, '3': 3,
    quatro: 4, quarto: 4, quarta: 4, '4': 4,
    cinco: 5, quinto: 5, quinta: 5, '5': 5,
  },
  es: {
    uno: 1, una: 1, primero: 1, primera: 1, '1': 1,
    dos: 2, segundo: 2, segunda: 2, '2': 2,
    tres: 3, tercero: 3, tercera: 3, '3': 3,
    cuatro: 4, cuarto: 4, cuarta: 4, '4': 4,
    cinco: 5, quinto: 5, quinta: 5, '5': 5,
  },
};

@Injectable({ providedIn: 'root' })
export class VoiceCarSelectionService {
  // Returns the 0-based index of the selected car, or null if ambiguous/not found.
  // Scans all words in the transcript against the map for the given language,
  // plus the 'it' map as a universal fallback (mechanics often use Italian
  // numbers regardless of detected language).
  parse(transcript: string, lang: string, listLength: number): number | null {
    const words = transcript.toLowerCase().replace(/[^a-zàáâãäåèéêëìíîïòóôõöùúûüýÿ0-9\s]/g, ' ').split(/\s+/).filter(Boolean);
    const map = NUMBER_MAPS[lang] ?? NUMBER_MAPS['it'];
    const itMap = NUMBER_MAPS['it'];

    const matched = new Set<number>();

    for (const word of words) {
      const fromLang = map[word];
      if (fromLang !== undefined && fromLang <= listLength) matched.add(fromLang);
      const fromIt = itMap[word];
      if (fromIt !== undefined && fromIt <= listLength) matched.add(fromIt);
    }

    if (matched.size === 1) {
      return [...matched][0] - 1; // convert to 0-based
    }
    return null; // 0 matches or ambiguous (>1 different numbers)
  }
}
