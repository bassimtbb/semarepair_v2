// §7 localized strings for voice mode TTS output and UI toasts.
// RULE: buildSpokenText() must never pass these through Gemini or rewrite them.
// They wrap raw document field values only; no paraphrase.

export type VoiceLang = 'it' | 'en' | 'fr' | 'pt' | 'es';

interface S {
  car_selection_prefix: (n: number) => string;
  car_option: (n: number, label: string) => string;
  car_selection_prompt: string;
  car_selection_too_many: string;
  not_found: string;
  too_vague: string;
  causa_prefix: string;
  intervento_prefix: string;
  found_n_cases: (n: number) => string;
  see_screen: string;
  case_option: (badge: number, label: string) => string;
  case_selection_prompt: string;
  case_too_many: (n: number) => string;
  mic_unavailable_toast: string;
  transcribe_failed_toast: string;
  no_speech_hint: string;
  voice_unavailable_toast: string;
  hd_voice_unavailable_toast: string;

  // Extension v2 - technical answers. Same rule as everything above: these
  // wrap raw database values, they never paraphrase one.
  tech_fact: (reference: string | null, label: string, value: string) => string;
  tech_more_results: (n: number) => string;
  tech_schema: (title: string) => string;
  tech_offer_procedure: (title: string) => string;

  // Screen commands. The only strings in this file that describe something
  // the interface did rather than something the archive says - so they carry
  // the diagram's own title and nothing else.
  screen_enlarged: (title: string) => string;
  screen_reduced: string;

  // Spoken once, on the way out. Short on purpose: the mechanic has already
  // said he is done, so anything longer is talking over him.
  goodbye: string;

  // Une page de manuel scanne. La seule chaine de ce fichier qui entoure
  // du texte que personne n'a ecrit : l'OCR l'a devine. On nomme la page
  // et on renvoie a l'ecran, on n'en lit jamais le contenu.
  tech_manual: (title: string) => string;
}

const STRINGS: Record<VoiceLang, S> = {
  it: {
    car_selection_prefix: n => `Ho trovato ${n} veicoli compatibili:`,
    car_option: (n, l) => `${n}: ${l}.`,
    car_selection_prompt: 'Quale vuoi?',
    car_selection_too_many: 'Ho trovato molti veicoli. Puoi dirmi marca e modello per restringere la ricerca?',
    not_found: 'Non ho trovato documenti per questo guasto.',
    too_vague: 'La descrizione è troppo vaga. Puoi darmi più dettagli o un codice guasto?',
    causa_prefix: 'Causa:',
    intervento_prefix: 'Intervento:',
    found_n_cases: n => `Ho trovato ${n} casi.`,
    see_screen: 'Guarda lo schermo per gli altri casi.',
    case_option: (n, l) => `${n}: ${l}.`,
    case_selection_prompt: 'Quale vuoi?',
    case_too_many: n => `Ho trovato ${n} casi. Guarda lo schermo e dimmi il numero.`,
    mic_unavailable_toast: 'Microfono non disponibile. Controlla i permessi.',
    transcribe_failed_toast: 'Trascrizione fallita. Riprovo…',
    no_speech_hint: 'Non ho sentito nulla. Parla vicino al microfono.',
    voice_unavailable_toast: 'Voce non disponibile su questo browser — prova Voice HD.',
    hd_voice_unavailable_toast: 'Voce HD non disponibile.',
    tech_fact: (r, l, v) => (r ? `${r}, ${l}: ${v}.` : `${l}: ${v}.`),
    tech_more_results: n => `Ho trovato altri ${n} risultati sullo schermo.`,
    tech_schema: title => `Ho trovato lo schema elettrico ${title}. È sullo schermo.`,
    tech_offer_procedure: title => `Ho trovato la procedura ${title}. Vuoi che te la legga?`,
    screen_enlarged: title => title ? `Ho ingrandito lo schema ${title}.` : 'Ho ingrandito lo schema.',
    screen_reduced: 'Ho ridotto lo schema.',
    goodbye: 'A presto. Buon lavoro.',
    tech_manual: title => title ? `Ho trovato una pagina del manuale: ${title}. È sullo schermo.` : 'Ho trovato una pagina del manuale. È sullo schermo.',
  },
  en: {
    car_selection_prefix: n => `I found ${n} compatible vehicles:`,
    car_option: (n, l) => `${n}: ${l}.`,
    car_selection_prompt: 'Which one do you want?',
    car_selection_too_many: 'I found many vehicles. Can you give me the make and model to narrow it down?',
    not_found: 'I did not find any documents for this fault.',
    too_vague: 'The description is too vague. Can you give me more details or a fault code?',
    causa_prefix: 'Cause:',
    intervento_prefix: 'Procedure:',
    found_n_cases: n => `I found ${n} cases.`,
    see_screen: 'Check the screen for the other cases.',
    case_option: (n, l) => `${n}: ${l}.`,
    case_selection_prompt: 'Which one do you want?',
    case_too_many: n => `I found ${n} cases. Look at the screen and say the number.`,
    mic_unavailable_toast: 'Microphone unavailable. Check permissions.',
    transcribe_failed_toast: 'Transcription failed. Retrying…',
    no_speech_hint: 'I did not hear anything. Speak close to the microphone.',
    voice_unavailable_toast: 'Voice not available on this browser — try Voice HD.',
    hd_voice_unavailable_toast: 'Voice HD not available.',
    tech_fact: (r, l, v) => (r ? `${r}, ${l}: ${v}.` : `${l}: ${v}.`),
    tech_more_results: n => `I found ${n} more results on the screen.`,
    tech_schema: title => `I found the ${title} wiring diagram. It is on the screen.`,
    tech_offer_procedure: title => `I found the ${title} procedure. Shall I read it to you?`,
    screen_enlarged: title => title ? `I have enlarged the ${title} diagram.` : 'I have enlarged the diagram.',
    screen_reduced: 'I have made the diagram smaller again.',
    goodbye: 'Goodbye. Good luck with the job.',
    tech_manual: title => title ? `I found a page of the manual: ${title}. It is on the screen.` : 'I found a page of the manual. It is on the screen.',
  },
  fr: {
    car_selection_prefix: n => `J'ai trouvé ${n} véhicules compatibles :`,
    car_option: (n, l) => `${n} : ${l}.`,
    car_selection_prompt: 'Lequel voulez-vous ?',
    car_selection_too_many: "J'ai trouvé beaucoup de véhicules. Pouvez-vous me donner la marque et le modèle pour affiner ?",
    not_found: "Je n'ai trouvé aucun document pour ce défaut.",
    too_vague: 'La description est trop vague. Pouvez-vous me donner plus de détails ou un code défaut ?',
    causa_prefix: 'Cause :',
    intervento_prefix: 'Intervention :',
    found_n_cases: n => `J'ai trouvé ${n} cas.`,
    see_screen: "Regardez l'écran pour les autres cas.",
    case_option: (n, l) => `${n} : ${l}.`,
    case_selection_prompt: 'Lequel voulez-vous ?',
    case_too_many: n => `J'ai trouvé ${n} cas. Regardez l'écran et dites le numéro.`,
    mic_unavailable_toast: 'Microphone indisponible. Vérifiez les autorisations.',
    transcribe_failed_toast: 'Transcription échouée. Nouvelle tentative…',
    no_speech_hint: "Je n'ai rien entendu. Parlez près du microphone.",
    voice_unavailable_toast: 'Voix non disponible sur ce navigateur — essayez Voice HD.',
    hd_voice_unavailable_toast: 'Voice HD non disponible.',
    tech_fact: (r, l, v) => (r ? `${r}, ${l} : ${v}.` : `${l} : ${v}.`),
    tech_more_results: n => `J'ai trouvé ${n} autres résultats à l'écran.`,
    tech_schema: title => `J'ai trouvé le schéma électrique ${title}. Il est à l'écran.`,
    tech_offer_procedure: title => `J'ai trouvé la procédure ${title}. Voulez-vous que je vous la lise ?`,
    screen_enlarged: title => title ? `J'ai agrandi le schéma ${title}.` : "J'ai agrandi le schéma.",
    screen_reduced: "J'ai réduit le schéma.",
    goodbye: "À bientôt. Bon travail.",
    tech_manual: title => title ? `J'ai trouvé une page du manuel : ${title}. Elle est à l'écran.` : "J'ai trouvé une page du manuel. Elle est à l'écran.",
  },
  pt: {
    car_selection_prefix: n => `Encontrei ${n} veículos compatíveis:`,
    car_option: (n, l) => `${n}: ${l}.`,
    car_selection_prompt: 'Qual quer?',
    car_selection_too_many: 'Encontrei muitos veículos. Pode indicar a marca e o modelo para filtrar?',
    not_found: 'Não encontrei documentos para esta avaria.',
    too_vague: 'A descrição é vaga. Pode dar mais detalhes ou um código de avaria?',
    causa_prefix: 'Causa:',
    intervento_prefix: 'Intervenção:',
    found_n_cases: n => `Encontrei ${n} casos.`,
    see_screen: 'Veja o ecrã para os outros casos.',
    case_option: (n, l) => `${n}: ${l}.`,
    case_selection_prompt: 'Qual quer?',
    case_too_many: n => `Encontrei ${n} casos. Olhe para o ecrã e diga o número.`,
    mic_unavailable_toast: 'Microfone indisponível. Verifique as permissões.',
    transcribe_failed_toast: 'Transcrição falhou. A tentar novamente…',
    no_speech_hint: 'Não ouvi nada. Fale perto do microfone.',
    voice_unavailable_toast: 'Voz não disponível neste navegador — experimente Voice HD.',
    hd_voice_unavailable_toast: 'Voice HD não disponível.',
    tech_fact: (r, l, v) => (r ? `${r}, ${l}: ${v}.` : `${l}: ${v}.`),
    tech_more_results: n => `Encontrei mais ${n} resultados no ecrã.`,
    tech_schema: title => `Encontrei o esquema elétrico ${title}. Está no ecrã.`,
    tech_offer_procedure: title => `Encontrei o procedimento ${title}. Quer que lho leia?`,
    screen_enlarged: title => title ? `Ampliei o esquema ${title}.` : 'Ampliei o esquema.',
    screen_reduced: 'Reduzi o esquema.',
    goodbye: 'Até breve. Bom trabalho.',
    tech_manual: title => title ? `Encontrei uma página do manual: ${title}. Está no ecrã.` : 'Encontrei uma página do manual. Está no ecrã.',
  },
  es: {
    car_selection_prefix: n => `Encontré ${n} vehículos compatibles:`,
    car_option: (n, l) => `${n}: ${l}.`,
    car_selection_prompt: '¿Cuál quieres?',
    car_selection_too_many: 'Encontré muchos vehículos. ¿Puede decirme la marca y el modelo para filtrar?',
    not_found: 'No encontré documentos para este fallo.',
    too_vague: 'La descripción es demasiado vaga. ¿Puede darme más detalles o un código de fallo?',
    causa_prefix: 'Causa:',
    intervento_prefix: 'Intervención:',
    found_n_cases: n => `Encontré ${n} casos.`,
    see_screen: 'Mira la pantalla para los otros casos.',
    case_option: (n, l) => `${n}: ${l}.`,
    case_selection_prompt: '¿Cuál quieres?',
    case_too_many: n => `Encontré ${n} casos. Mira la pantalla y dime el número.`,
    mic_unavailable_toast: 'Micrófono no disponible. Compruebe los permisos.',
    transcribe_failed_toast: 'Transcripción fallida. Reintentando…',
    no_speech_hint: 'No escuché nada. Hable cerca del micrófono.',
    voice_unavailable_toast: 'Voz no disponible en este navegador — pruebe Voice HD.',
    hd_voice_unavailable_toast: 'Voice HD no disponible.',
    tech_fact: (r, l, v) => (r ? `${r}, ${l}: ${v}.` : `${l}: ${v}.`),
    tech_more_results: n => `Encontré ${n} resultados más en la pantalla.`,
    tech_schema: title => `Encontré el esquema eléctrico ${title}. Está en la pantalla.`,
    tech_offer_procedure: title => `Encontré el procedimiento ${title}. ¿Quieres que te lo lea?`,
    screen_enlarged: title => title ? `He ampliado el esquema ${title}.` : 'He ampliado el esquema.',
    screen_reduced: 'He reducido el esquema.',
    goodbye: 'Hasta pronto. Buen trabajo.',
    tech_manual: title => title ? `Encontré una página del manual: ${title}. Está en la pantalla.` : 'Encontré una página del manual. Está en la pantalla.',
  },
};

function lang(l: string): VoiceLang {
  return (l in STRINGS ? l : 'it') as VoiceLang;
}

// String keys only (not function-valued keys). hd_voice_unavailable_toast
// is used in VoiceModeService when engine==='google' and speak() rejects.
export type StringKey = Exclude<
  keyof S,
  | 'car_selection_prefix' | 'car_option' | 'found_n_cases' | 'case_option' | 'case_too_many'
  | 'tech_fact' | 'tech_more_results' | 'tech_schema' | 'tech_offer_procedure'
  | 'screen_enlarged' | 'tech_manual'
>;

export function t(l: string, key: StringKey): string {
  return STRINGS[lang(l)][key];
}

export function tCarSelectionPrefix(l: string, n: number): string {
  return STRINGS[lang(l)].car_selection_prefix(n);
}

export function tCarOption(l: string, n: number, label: string): string {
  return STRINGS[lang(l)].car_option(n, label);
}

export function tFoundNCases(l: string, n: number): string {
  return STRINGS[lang(l)].found_n_cases(n);
}

export function tCaseOption(l: string, badge: number, label: string): string {
  return STRINGS[lang(l)].case_option(badge, label);
}

export function tCaseTooMany(l: string, n: number): string {
  return STRINGS[lang(l)].case_too_many(n);
}

export function tTechFact(l: string, reference: string | null, label: string, value: string): string {
  return STRINGS[lang(l)].tech_fact(reference, label, value);
}

export function tTechMoreResults(l: string, n: number): string {
  return STRINGS[lang(l)].tech_more_results(n);
}

export function tTechSchema(l: string, title: string): string {
  return STRINGS[lang(l)].tech_schema(title);
}

export function tTechOfferProcedure(l: string, title: string): string {
  return STRINGS[lang(l)].tech_offer_procedure(title);
}

export function tScreenEnlarged(l: string, title: string): string {
  return STRINGS[lang(l)].screen_enlarged(title);
}

export function tTechManual(l: string, title: string): string {
  return STRINGS[lang(l)].tech_manual(title);
}
