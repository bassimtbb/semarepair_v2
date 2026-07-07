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
  mic_unavailable_toast: string;
  transcribe_failed_toast: string;
  no_speech_hint: string;
  voice_unavailable_toast: string;
  hd_voice_unavailable_toast: string;
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
    found_n_cases: n => `Ho trovato ${n} casi. Il più rilevante —`,
    see_screen: 'Guarda lo schermo per gli altri casi.',
    mic_unavailable_toast: 'Microfono non disponibile. Controlla i permessi.',
    transcribe_failed_toast: 'Trascrizione fallita. Riprovo…',
    no_speech_hint: 'Non ho sentito nulla. Parla vicino al microfono.',
    voice_unavailable_toast: 'Voce non disponibile su questo browser — prova Voice HD.',
    hd_voice_unavailable_toast: 'Voce HD non disponibile.',
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
    found_n_cases: n => `I found ${n} cases. The most relevant —`,
    see_screen: 'Check the screen for the other cases.',
    mic_unavailable_toast: 'Microphone unavailable. Check permissions.',
    transcribe_failed_toast: 'Transcription failed. Retrying…',
    no_speech_hint: 'I did not hear anything. Speak close to the microphone.',
    voice_unavailable_toast: 'Voice not available on this browser — try Voice HD.',
    hd_voice_unavailable_toast: 'Voice HD not available.',
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
    found_n_cases: n => `J'ai trouvé ${n} cas. Le plus pertinent —`,
    see_screen: "Regardez l'écran pour les autres cas.",
    mic_unavailable_toast: 'Microphone indisponible. Vérifiez les autorisations.',
    transcribe_failed_toast: 'Transcription échouée. Nouvelle tentative…',
    no_speech_hint: "Je n'ai rien entendu. Parlez près du microphone.",
    voice_unavailable_toast: 'Voix non disponible sur ce navigateur — essayez Voice HD.',
    hd_voice_unavailable_toast: 'Voice HD non disponible.',
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
    found_n_cases: n => `Encontrei ${n} casos. O mais relevante —`,
    see_screen: 'Veja o ecrã para os outros casos.',
    mic_unavailable_toast: 'Microfone indisponível. Verifique as permissões.',
    transcribe_failed_toast: 'Transcrição falhou. A tentar novamente…',
    no_speech_hint: 'Não ouvi nada. Fale perto do microfone.',
    voice_unavailable_toast: 'Voz não disponível neste navegador — experimente Voice HD.',
    hd_voice_unavailable_toast: 'Voice HD não disponível.',
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
    found_n_cases: n => `Encontré ${n} casos. El más relevante —`,
    see_screen: 'Mira la pantalla para los otros casos.',
    mic_unavailable_toast: 'Micrófono no disponible. Compruebe los permisos.',
    transcribe_failed_toast: 'Transcripción fallida. Reintentando…',
    no_speech_hint: 'No escuché nada. Hable cerca del micrófono.',
    voice_unavailable_toast: 'Voz no disponible en este navegador — pruebe Voice HD.',
    hd_voice_unavailable_toast: 'Voice HD no disponible.',
  },
};

function lang(l: string): VoiceLang {
  return (l in STRINGS ? l : 'it') as VoiceLang;
}

// String keys only (not function-valued keys). hd_voice_unavailable_toast
// is used in VoiceModeService when engine==='google' and speak() rejects.
export type StringKey = Exclude<keyof S, 'car_selection_prefix' | 'car_option' | 'found_n_cases'>;

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
