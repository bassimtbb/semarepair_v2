// Commands about the SCREEN rather than about the vehicle.
//
// "Poi ingrandisci questo schema" is not a question the archive can answer -
// it is an instruction to the interface. Sent to the model it produced a
// confident apology: *"non ho la capacità grafica di ingrandire le immagini"*,
// followed by advice to press Ctrl + "+". The product does have that control,
// sitting in the diagram's own title bar; the model simply has no way to know
// it exists. So the command is recognised here and carried out, never asked
// about.
//
// Deterministic string matching, in the five languages, for the same reason
// the shared-engine disclosure is built in C# instead of being left to Gemini:
// a control the mechanic operates with his hands full must work every time,
// not most times. Nothing here reaches the network.
//
// Shrink is tested before enlarge deliberately: "esci da schermo intero"
// contains "schermo intero", so checking enlarge first would read an exit as
// an enter.

export type ScreenCommand = 'enlarge' | 'shrink';

type Patterns = { enlarge: RegExp; shrink: RegExp };

// Written as word-ish fragments rather than whole sentences: speech arrives
// without punctuation and with filler ("poi ingrandisci questo schema"), so
// anchoring to the start of the utterance would miss most real phrasings.
const PATTERNS: Record<string, Patterns> = {
  it: {
    shrink: /\b(riduci|ridurre|rimpicciolisci|esci\s+da(llo)?\s+schermo|torna\s+(indietro|piccolo)|più\s+piccolo|minimizza)\b/,
    enlarge: /\b(ingrandisci|ingrandire|ingrandiscilo|allarga|massimizza|schermo\s+intero|tutto\s+schermo|più\s+grande|zoom)\b/,
  },
  fr: {
    shrink: /\b(réduis|réduire|rapetisse|quitte(r)?\s+le\s+plein\s+écran|sors\s+du\s+plein\s+écran|plus\s+petit|minimise)\b/,
    enlarge: /\b(agrandis|agrandir|agrandis-le|plein\s+écran|plus\s+grand|maximise|zoome)\b/,
  },
  en: {
    shrink: /\b(shrink|smaller|exit\s+full\s*screen|leave\s+full\s*screen|minimi[sz]e|zoom\s+out|reduce)\b/,
    enlarge: /\b(enlarge|full\s*screen|bigger|larger|maximi[sz]e|zoom\s+in|blow\s+it\s+up)\b/,
  },
  es: {
    shrink: /\b(reduce|reducir|sal\s+de\s+pantalla\s+completa|más\s+pequeño|minimiza|aleja)\b/,
    enlarge: /\b(amplía|amplia|ampliar|agranda|pantalla\s+completa|más\s+grande|maximiza|acerca)\b/,
  },
  pt: {
    shrink: /\b(reduz|reduzir|diminui|diminuir|sai\s+do\s+(ecrã|ecra|modo)|menor|minimiza)\b/,
    enlarge: /\b(amplia|ampliar|aumenta|aumentar|ecrã\s+inteiro|ecra\s+inteiro|tela\s+cheia|maior|maximiza)\b/,
  },
};

function normalise(text: string): string {
  return text
    .toLowerCase()
    // Speech-to-text and typing both produce these; they only get in the way
    // of a word-boundary match.
    .replace(/[.,;:!?¿¡"'()]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

/**
 * Returns the screen command the text asks for, or null when it asks for
 * something else. Falls back to Italian patterns for an unrecognised
 * language, matching how the rest of voice mode treats `detectedLanguage`.
 */
export function parseScreenCommand(text: string, lang: string): ScreenCommand | null {
  const patterns = PATTERNS[lang.toLowerCase().slice(0, 2)] ?? PATTERNS['it'];
  const normalised = normalise(text);
  if (!normalised) return null;

  if (patterns.shrink.test(normalised)) return 'shrink';
  if (patterns.enlarge.test(normalised)) return 'enlarge';
  return null;
}
