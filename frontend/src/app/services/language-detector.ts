// '/light' trades some accuracy for a much smaller bundled n-gram model -
// fine here since detectLanguage() is only ever choosing among 5 candidates.
import { detectAll } from 'tinyld/light';

// Matches SystemPromptBuilder.LanguageNames in the Chat Service - those
// are the only languages Gemini is instructed to reply in.
const SUPPORTED_LANGUAGES = ['it', 'en', 'fr', 'pt', 'es'];

// Below this many *real words*, tinyld's n-gram model has too little
// signal to trust *regardless of its reported accuracy score* - confirmed
// by real testing: a one-off reply like "0.9 TwinAir" (an engine label,
// not even real prose) scored "fr" at the same ~0.14 magnitude a genuine
// Italian sentence scores for "it" - the score alone doesn't separate a
// confident detection from a coin-flip on short input, only word count
// reliably does. A real conversation hit this: a single short reply
// flipped the session's detected language, and every message afterward -
// including a fully templated "not found" reply - silently inherited the
// wrong one. Below this floor, skip detection entirely and keep whatever
// language the conversation was already in.
const MIN_WORDS_FOR_DETECTION = 3;

// A token counts as a real word only if it's letters-only (no digits) and
// at least 2 characters after stripping punctuation - confirmed by real
// testing: "si, 0.9 TwinAir" has 3 whitespace-separated tokens (clearing a
// naive word count) but only one real word ("si"); "0.9" and "TwinAir"
// are a number and an engine-trim label, not prose, and together they
// dominated tinyld's scoring enough to call the whole message French.
// Counting only genuine words keeps that message correctly below
// MIN_WORDS_FOR_DETECTION instead of letting numbers/codes pad the count.
function countRealWords(text: string): number {
  return text
    .trim()
    .split(/\s+/)
    .filter(token => {
      const letters = token.replace(/[^\p{L}]/gu, '');
      return letters.length >= 2 && !/\d/.test(token);
    }).length;
}

// Below this ratio between the top candidate's accuracy and the
// runner-up's, the top pick isn't meaningfully ahead - just a coin-flip
// tinyld happened to call one way. Confirmed by real testing: a genuine
// 4-word Italian symptom report, "Accensione spia avaria motore", scored
// "pt" 0.0385 vs "it" 0.0337 (ratio 1.14) - Portuguese edged out Italian
// because "avaria"/"motore" share roots with Portuguese/Spanish cognates.
// A second real report surfaced later, "A motore caldo accensione spia
// avaria motore e avaria generica" (more "avaria"/"motore" repetition),
// scored an even higher ratio of 1.36 - still wrong, and still above the
// original 1.3 threshold meant to catch exactly this class of bug, so
// that document request still went out in the wrong language. Both wrong
// cases now top out at 1.36; every genuinely confident, correctly
// detected phrase tested alongside them (in both directions - IT/PT/EN/
// FR/ES) scored 1.88 or higher, a clean gap. Below this floor, keep
// whatever language the conversation was already in instead of trusting
// the coin-flip - same "don't flap on weak signal" rule as the word-count
// floor above.
const MIN_CONFIDENCE_RATIO = 1.6;

// Restricting tinyld to these 5 candidates (instead of its full ~600
// language model) is what makes short technical phrases resolve at all -
// unrestricted, "ok grazie" matched Polish over Italian.
export function detectLanguage(text: string, fallback: string): string {
  if (countRealWords(text) < MIN_WORDS_FOR_DETECTION) return fallback;

  const matches = detectAll(text, { only: SUPPORTED_LANGUAGES });
  if (matches.length === 0) return fallback;

  const [top, second] = matches;
  if (second && top.accuracy / second.accuracy < MIN_CONFIDENCE_RATIO) {
    return fallback;
  }
  return top.lang;
}
