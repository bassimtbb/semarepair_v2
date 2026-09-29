// "Niente, grazie." - the mechanic is done.
//
// Observed live: the sign-off was routed to the backend like any other
// question, answered politely ("Prego! Rimango a disposizione..."), and voice
// mode went straight back to listening. Nobody was speaking, so the next
// recording caught room noise, and the turn after that was a transcription
// failure. Hands-free mode has no obvious way out from under a vehicle, so a
// spoken goodbye has to be one.
//
// Matched against the WHOLE utterance, unlike the screen commands in
// screen-commands.ts which deliberately match mid-sentence. The difference is
// the cost of being wrong: enlarging a diagram nobody asked to enlarge is a
// shrug, ending the session mid-repair is not. "Grazie per la procedura, ora
// dimmi che fusibile protegge l'ABS" must never stop anything, and anchoring
// is what guarantees that.
//
// Screen words are deliberately absent: "esci", "chiudi", "close", "exit",
// "sair" and friends belong to screen-commands.ts. A mechanic looking at a
// fullscreen diagram who says "esci" wants the diagram back to size, not the
// microphone switched off, and the two cannot both claim the word. The
// multi-word forms there ("esci da schermo intero") are unaffected either way,
// since this file only ever matches a whole utterance.
//
// Bare "grazie" / "thanks" IS treated as a goodbye. It is a judgement call:
// said alone, it almost always closes an exchange, and the cost of being wrong
// is one press of the button, against the cost of the loop above. Said as part
// of a longer sentence it is not matched at all.

const OPTIONAL_LEAD = String.raw`(?:ok|okay|va\s+bene|allora|ecco|beh|bon|bueno|bom)?[\s,]*`;
const OPTIONAL_TAIL = String.raw`[\s,.!]*`;

function whole(core: string): RegExp {
  return new RegExp(`^${OPTIONAL_LEAD}(?:${core})${OPTIONAL_TAIL}$`);
}

const FAREWELLS: Record<string, RegExp> = {
  it: whole(String.raw`
      (?:niente|nulla|no)[\s,]+grazie
    | grazie(?:\s+(?:mille|tante|di\s+tutto))?
    | ciao | arrivederci | arrivederla | addio | a\s+presto
    | basta(?:\s+cos[ìi])?
    | ho\s+finito | abbiamo\s+finito | ho\s+gi[àa]\s+finito
    | (?:tutto\s+)?a\s+posto
    | (?:per\s+ora\s+)?[èe]\s+tutto
    | stop | ferma(?:ti)?
  `.replace(/\s+/g, '')),
  fr: whole(String.raw`
      (?:non|rien)[\s,]+merci
    | merci(?:\s+(?:beaucoup|bien))?
    | au\s+revoir | salut | [àa]\s+bient[ôo]t | bonne\s+journ[ée]e
    | [çc]a\s+suffit | c'?est\s+bon | c'?est\s+tout
    | j'?ai\s+fini | on\s+a\s+fini
    | stop | arr[êe]te
  `.replace(/\s+/g, '')),
  en: whole(String.raw`
      (?:no|nothing)[\s,]+thanks?(?:\s+you)?
    | thanks?(?:\s+you)?(?:\s+(?:a\s+lot|very\s+much))?
    | goodbye | bye(?:\s+bye)? | see\s+you | cheers
    | that'?s\s+(?:all|it) | that\s+is\s+all
    | i'?m\s+done | we'?re\s+done | all\s+done | we'?re\s+good
    | stop
  `.replace(/\s+/g, '')),
  es: whole(String.raw`
      (?:no|nada)[\s,]+gracias
    | gracias(?:\s+(?:mil|much[ao]s))?
    | adi[óo]s | hasta\s+luego | hasta\s+pronto | chao
    | ya\s+est[áa] | eso\s+es\s+todo | es\s+todo
    | he\s+terminado | hemos\s+terminado
    | basta | parar
  `.replace(/\s+/g, '')),
  pt: whole(String.raw`
      (?:n[ãa]o|nada)[\s,]+obrigad[oa]
    | obrigad[oa](?:\s+(?:muito|por\s+tudo))?
    | adeus | tchau | at[ée]\s+logo | at[ée]\s+breve
    | j[áa]\s+est[áa] | [ée]\s+tudo | isso\s+[ée]\s+tudo
    | terminei | acabei | j[áa]\s+terminei
    | parar
  `.replace(/\s+/g, '')),
};

function normalise(text: string): string {
  return text
    .toLowerCase()
    .replace(/[¿¡"'()]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
}

/**
 * True when the whole utterance is a sign-off and nothing else.
 *
 * Falls back to Italian for an unrecognised language, matching how the rest of
 * voice mode treats `detectedLanguage`.
 */
export function isFarewell(text: string, lang: string): boolean {
  const pattern = FAREWELLS[lang.toLowerCase().slice(0, 2)] ?? FAREWELLS['it'];
  const normalised = normalise(text);
  if (!normalised) return false;
  return pattern.test(normalised);
}
