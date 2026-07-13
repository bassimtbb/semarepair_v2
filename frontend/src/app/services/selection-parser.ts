// Combined number-word table (1-based) for all 5 supported languages.
// Keys are pre-normalised: lowercase, accent-stripped, no punctuation.
// Multi-word compound numbers (e.g. "twenty eight", "dix sept", "vinte e um")
// are stored as space-separated strings and matched via n-gram scanning.
//
// Digits '1'–'30' are included as string keys so bare-digit inputs work
// without special-casing — the scanner sees "8" as a 1-gram token and
// looks it up here like any word.
const WORDS: Record<string, number> = {
  // ── Digits 1–30 ──────────────────────────────────────────────────────────
  '1': 1,  '2': 2,  '3': 3,  '4': 4,  '5': 5,
  '6': 6,  '7': 7,  '8': 8,  '9': 9,  '10': 10,
  '11': 11, '12': 12, '13': 13, '14': 14, '15': 15,
  '16': 16, '17': 17, '18': 18, '19': 19, '20': 20,
  '21': 21, '22': 22, '23': 23, '24': 24, '25': 25,
  '26': 26, '27': 27, '28': 28, '29': 29, '30': 30,

  // ── ITALIAN cardinals ─────────────────────────────────────────────────────
  uno: 1,  una: 1,
  due: 2,
  tre: 3,
  quattro: 4,
  cinque: 5,
  sei: 6,
  sette: 7,
  otto: 8,
  nove: 9,
  dieci: 10,
  undici: 11,
  dodici: 12,
  tredici: 13,
  quattordici: 14,
  quindici: 15,
  sedici: 16,
  diciassette: 17,
  diciotto: 18,
  diciannove: 19,
  venti: 20,
  ventuno: 21,
  ventidue: 22,
  ventitre: 23,
  ventiquattro: 24,
  venticinque: 25,
  ventisei: 26,
  ventisette: 27,
  ventotto: 28,
  ventinove: 29,
  trenta: 30,
  // Italian ordinals (m/f)
  primo: 1,   prima: 1,
  secondo: 2, seconda: 2,
  terzo: 3,   terza: 3,
  quarto: 4,  quarta: 4,
  quinto: 5,  quinta: 5,
  sesto: 6,   sesta: 6,
  settimo: 7, settima: 7,
  ottavo: 8,  ottava: 8,
  nono: 9,    nona: 9,
  decimo: 10, decima: 10,
  undicesimo: 11,      undicesima: 11,
  dodicesimo: 12,      dodicesima: 12,
  tredicesimo: 13,     tredicesima: 13,
  quattordicesimo: 14, quattordicesima: 14,
  quindicesimo: 15,    quindicesima: 15,
  sedicesimo: 16,      sedicesima: 16,
  diciassettesimo: 17, diciassettesima: 17,
  diciottesimo: 18,    diciottesima: 18,
  diciannovesimo: 19,  diciannovesima: 19,
  ventesimo: 20,       ventesima: 20,

  // ── ENGLISH cardinals ─────────────────────────────────────────────────────
  one: 1,
  two: 2,
  three: 3,
  four: 4,
  five: 5,
  six: 6,        // also French "six" = 6 — same string, same value
  seven: 7,
  eight: 8,
  nine: 9,
  ten: 10,
  eleven: 11,
  twelve: 12,
  thirteen: 13,
  fourteen: 14,
  fifteen: 15,
  sixteen: 16,
  seventeen: 17,
  eighteen: 18,
  nineteen: 19,
  twenty: 20,
  'twenty one': 21,
  'twenty two': 22,
  'twenty three': 23,
  'twenty four': 24,
  'twenty five': 25,
  'twenty six': 26,
  'twenty seven': 27,
  'twenty eight': 28,
  'twenty nine': 29,
  thirty: 30,
  // English ordinals
  first: 1,
  second: 2,
  third: 3,
  fourth: 4,
  fifth: 5,
  sixth: 6,
  seventh: 7,
  eighth: 8,
  ninth: 9,
  tenth: 10,
  eleventh: 11,
  twelfth: 12,
  thirteenth: 13,
  fourteenth: 14,
  fifteenth: 15,
  sixteenth: 16,
  seventeenth: 17,
  eighteenth: 18,
  nineteenth: 19,
  twentieth: 20,

  // ── FRENCH cardinals (skip "un"/"une" — too common as articles) ───────────
  deux: 2,
  trois: 3,
  quatre: 4,
  cinq: 5,
  // six: already present from English (same string, same value)
  sept: 7,
  huit: 8,
  neuf: 9,
  dix: 10,
  onze: 11,  // also Portuguese "onze" = 11 — same string, same value
  douze: 12,
  treize: 13,
  quatorze: 14,
  quinze: 15,  // also Portuguese "quinze" = 15 — same string, same value
  seize: 16,
  'dix sept': 17,
  'dix huit': 18,
  'dix neuf': 19,
  vingt: 20,
  'vingt et un': 21,   // 3-gram
  'vingt deux': 22,
  'vingt trois': 23,
  'vingt quatre': 24,
  'vingt cinq': 25,
  'vingt six': 26,
  'vingt sept': 27,
  'vingt huit': 28,
  'vingt neuf': 29,
  trente: 30,
  // French ordinals
  premier: 1, premiere: 1,
  deuxieme: 2,
  troisieme: 3,
  quatrieme: 4,
  cinquieme: 5,
  sixieme: 6,
  septieme: 7,
  huitieme: 8,
  neuvieme: 9,
  dixieme: 10,

  // ── PORTUGUESE cardinals (skip "um"/"uma" — article-like) ────────────────
  dois: 2, duas: 2,
  tres: 3,        // PT "três" and ES "tres" both normalise to "tres" = 3
  quatro: 4,      // different from IT "quattro"
  cinco: 5,       // PT and ES both use "cinco"
  seis: 6,        // PT and ES both use "seis"
  sete: 7,
  oito: 8,
  // nove: 9 — same string as IT "nove", already present
  dez: 10,
  // onze: 11 — already present (FR/PT share this string)
  doze: 12,
  treze: 13,
  catorze: 14,
  // quinze: 15 — already present (FR/PT share this string)
  dezasseis: 16, dezesseis: 16,
  dezassete: 17, dezessete: 17,
  dezoito: 18,
  dezanove: 19,  dezenove: 19,
  vinte: 20,
  'vinte e um': 21,  'vinte e uma': 21,    // 3-grams
  'vinte e dois': 22, 'vinte e duas': 22,
  'vinte e tres': 23,
  'vinte e quatro': 24,
  'vinte e cinco': 25,
  'vinte e seis': 26,
  'vinte e sete': 27,
  'vinte e oito': 28,
  'vinte e nove': 29,
  trinta: 30,
  // Portuguese ordinals
  primeiro: 1,  primeira: 1,
  // segundo/segunda: already in IT table as different spelling ("secondo" ≠ "segundo")
  terceiro: 3,  terceira: 3,
  // quarto/quarta: same string as IT, already present (same value)
  // quinto/quinta: same string as IT, already present (same value)
  sexto: 6,   sexta: 6,
  setimo: 7,  setima: 7,    // "sétimo/sétima" → "setimo/setima" after accent strip
  oitavo: 8,  oitava: 8,
  // nono/nona: same string as IT, already present (same value)
  // decimo/decima: same string as IT, already present (same value)

  // ── SPANISH cardinals ─────────────────────────────────────────────────────
  // uno/una: same string as IT, already present (same value)
  dos: 2,
  // tres: already present (PT/ES share this normalised form)
  cuatro: 4,
  // cinco: already present (PT/ES share this string)
  // seis: already present
  siete: 7,
  ocho: 8,
  nueve: 9,
  diez: 10,
  once: 11,
  doce: 12,
  trece: 13,
  catorce: 14,
  quince: 15,
  dieciseis: 16,
  diecisiete: 17,
  dieciocho: 18,
  diecinueve: 19,
  veinte: 20,
  veintiuno: 21, veintiuna: 21,
  veintidos: 22,
  veintitres: 23,
  veinticuatro: 24,
  veinticinco: 25,
  veintiseis: 26,
  veintisiete: 27,
  veintiocho: 28,
  veintinueve: 29,
  treinta: 30,
  // Spanish ordinals
  primero: 1,  primera: 1,
  segundo: 2,  segunda: 2,
  tercero: 3,  tercera: 3,
  cuarto: 4,   cuarta: 4,
  // quinto/quinta: already present
  // sexto/sexta: already present
  septimo: 7,  septima: 7,   // "séptimo/séptima" → "septimo/septima" after accent strip
  octavo: 8,   octava: 8,
  noveno: 9,   novena: 9,
  // decimo/decima: already present
};

// Tokens that may surround a number reference without indicating a sentence.
// Used by strict mode: if any non-consumed token is NOT in this set, the
// text is treated as an embedded-number sentence rather than a selection input.
// All entries are in normalised (accent-stripped, lowercase) form.
const STRICT_CONTEXT = new Set([
  // Articles / function words
  'il', 'la', 'lo', 'le', 'i', 'gli', 'l',  // IT
  'un', 'una',                                  // IT/ES/PT indefinite
  'the', 'a', 'an',                            // EN
  'les', 'des', 'du', 'de', 'd',              // FR
  'o', 'os', 'as', 'um', 'uma',              // PT
  'el', 'los', 'las',                          // ES
  // Vehicle selection nouns / prefixes (accent-stripped)
  'numero', 'number',                          // IT/ES/PT "número"→"numero"; FR "numéro"→"numero"
  'auto', 'veicolo', 'macchina',              // IT
  'car', 'vehicle',                            // EN
  'voiture', 'vehicule',                       // FR (véhicule→vehicule)
  'carro', 'veiculo',                         // PT (veículo→veiculo)
  'coche',                                     // ES
  // Document selection nouns
  'caso', 'case', 'cas', 'documento', 'document', 'doc',
]);

function normalise(s: string): string {
  // NFD decomposition separates base letters from their accent marks;
  // the replace then removes only the accent code points (U+0300–U+036F).
  return s
    .toLowerCase()
    .normalize('NFD')
    .replace(/\p{Mn}/gu, '')          // strip all combining marks (accent stripping)
    .replace(/[^a-z0-9]/g, ' ')      // punctuation / apostrophes → space
    .replace(/\s+/g, ' ')
    .trim();
}

// Returns the 0-based visual-order index of the selected item, or null if:
//   • no number reference is found
//   • multiple different numbers are referenced (ambiguous)
//   • the found number exceeds listLength (out of range)
//   • strict=true AND any non-number token is not a known article/prefix
//     (detects "ho 2 auto" as an embedded-number sentence, not a selection)
//
// Scanning is greedy left-to-right: at each position the longest matching
// n-gram wins (3-gram → 2-gram → 1-gram). This prevents compound words like
// "vinte e quatro" from being split into two separate numbers (20 and 4).
//
// strict defaults to false (voice path). Pass true for the typed-text path
// where a sentence about a number (e.g. "ho 2 auto") must not trigger selection.
//
// lang is accepted for API symmetry with VoiceCarSelectionService.parse()
// but the table already covers all 5 languages simultaneously.
export function parseCarSelection(
  text: string,
  _lang: string,
  listLength: number,
  strict = false,
): number | null {
  const tokens = normalise(text).split(' ').filter(Boolean);
  const matched = new Set<number>();
  const consumed = new Set<number>(); // token indices consumed by number matches

  for (let i = 0; i < tokens.length; ) {
    let advanced = false;

    // Try 3-gram
    if (i + 2 < tokens.length) {
      const tri = `${tokens[i]} ${tokens[i + 1]} ${tokens[i + 2]}`;
      const v = WORDS[tri];
      if (v !== undefined) {
        if (v > listLength) return null;
        matched.add(v);
        consumed.add(i); consumed.add(i + 1); consumed.add(i + 2);
        i += 3;
        advanced = true;
      }
    }

    // Try 2-gram
    if (!advanced && i + 1 < tokens.length) {
      const bi = `${tokens[i]} ${tokens[i + 1]}`;
      const v = WORDS[bi];
      if (v !== undefined) {
        if (v > listLength) return null;
        matched.add(v);
        consumed.add(i); consumed.add(i + 1);
        i += 2;
        advanced = true;
      }
    }

    // Try 1-gram
    if (!advanced) {
      const v = WORDS[tokens[i]];
      if (v !== undefined) {
        if (v > listLength) return null;
        matched.add(v);
        consumed.add(i);
      }
      i++;
    }
  }

  if (matched.size !== 1) return null; // 0 matches or ambiguous

  if (strict) {
    // In strict mode every non-consumed token must be a known article or
    // selection prefix. Any real content word → this is a sentence with an
    // embedded number, not a selection input.
    for (let i = 0; i < tokens.length; i++) {
      if (!consumed.has(i) && !STRICT_CONTEXT.has(tokens[i])) return null;
    }
  }

  return [...matched][0] - 1; // 0-based
}
