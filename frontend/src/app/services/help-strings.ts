// Text for the help drawer, in the five languages the product speaks.
//
// Deliberately NOT in voice-strings.ts. That file's contract, stated in its
// first three lines, is that every string there wraps a raw archive value
// and is safe to read aloud. This text describes the interface instead - it
// is written by us, about us - so it lives beside it rather than inside it.
//
// Note what is NOT here any more: the suggested questions. They used to be
// four hardcoded Italian sentences, which only ever fitted one vehicle. They
// now come from /api/search/coverage - real anomalia texts, real chunk
// headings, the car's own fault codes - so a fifth vehicle brings its own
// without a line being written here. What remains is the labelling: section
// names, units, and the prose that frames them.
//
// The Record<HelpLanguage, HelpCopy> below is what forces all five
// translations to exist: adding a key and forgetting Portuguese does not
// ship a half-translated panel, it fails the build.

export type HelpLanguage = 'it' | 'en' | 'fr' | 'pt' | 'es';

export interface HelpCopy {
  title: string;
  close: string;

  // The chat box's own prompt - the one piece of chrome the reader looks at
  // continuously, so the one that makes the language menu feel real.
  placeholder: string;

  demo_badge: string;
  demo_title: string;
  demo_body: (vehicles: number) => string;

  choose_vehicle: string;
  choose_vehicle_hint: string;

  howto_title: string;
  howto_1: string;
  howto_2: string;
  howto_3: string;

  // One heading per kind of answer the product can give. A section whose
  // vehicle has nothing of that kind is not rendered at all - the BMW
  // carries no fault code, and showing no heading says so better than an
  // empty list under one that promises some.
  sec_cases: string;
  sec_photos: string;
  sec_codes: string;
  sec_diagrams: string;
  sec_manual: string;
  sec_technical: string;

  // Units for the count badge beside each heading. Short on purpose: the
  // number carries the meaning, the word only says what is being counted.
  unit_cases: string;
  unit_photos: string;
  unit_codes: string;
  unit_diagrams: string;
  unit_manual: string;
  unit_technical: string;

  // The archive gives labels, not questions: a chunk is headed
  // "CLIMATIZZAZIONE", and typing that alone returns NOTHING - measured. A
  // single upper-case word sits too far from the chunk for the vector, and
  // the router reads it as a system name and asks for clarification. These
  // stems turn a label into something a mechanic would actually type, and
  // they live here rather than in the API because they are interface
  // wording - the endpoint keeps returning the archive's own words.
  ask_data: (subject: string) => string;
  ask_diagram: (subject: string) => string;

  try_hint: string;
  codes_hint: string;

  // The whole list, by vehicle, folded away. A tester who types a code at
  // random gets a correct "not found" and concludes the product is broken -
  // which is what happened the first time a client tried it, with P1030.
  codes_all_title: string;
  codes_all_count: (codes: number) => string;

  // Said out loud for a vehicle that carries none. Leaving it off the list
  // was the first attempt, and an absence has to be NOTICED before it says
  // anything; a line that states it cannot be missed.
  codes_none: string;

  codes_group_p: string;
  codes_group_b: string;
  codes_group_c: string;
  codes_group_u: string;
  codes_group_other: string;

  voice_title: string;
  voice_body: string;
  voice_hd: string;
  voice_std: string;

  zoom_title: string;
  zoom_body: string;

  limits_title: string;
  limits_body: string;

  language_title: string;
  coverage_full: string;
  coverage_no_manual: string;
  coverage_data_only: string;
}

const COPY: Record<HelpLanguage, HelpCopy> = {
  it: {
    title: 'Come funziona',
    close: 'Chiudi',
    placeholder: 'Descrivi il problema o inserisci un codice guasto...',

    demo_badge: 'DEMO',
    demo_title: 'Questa è una dimostrazione',
    demo_body: n => n === 1
      ? 'L’archivio caricato è volutamente limitato: contiene un solo veicolo, con la documentazione reale dell’officina.'
      : `L’archivio caricato è volutamente limitato: contiene ${n} veicoli. Per ognuno la documentazione è quella reale dell’officina, e non è la stessa per tutti.`,

    choose_vehicle: 'Scegli il veicolo',
    choose_vehicle_hint: 'Tocca un veicolo per cercarlo.',

    howto_title: 'Come si usa',
    howto_1: 'Scegli il veicolo qui sopra, o scrivilo nel campo',
    howto_2: 'Conferma la scheda che compare',
    howto_3: 'Fai la tua domanda tecnica',

    sec_cases: 'Casi di guasto',
    sec_photos: 'Con foto',
    sec_codes: 'Codici guasto',
    sec_diagrams: 'Schemi elettrici',
    sec_manual: 'Manuale d’officina',
    sec_technical: 'Dati tecnici',

    unit_cases: 'casi',
    unit_photos: 'foto',
    unit_codes: 'codici',
    unit_diagrams: 'schemi',
    unit_manual: 'pagine',
    unit_technical: 'dati',

    ask_data: subject => `Mostrami i dati tecnici di ${subject}`,
    ask_diagram: subject => `Mostrami lo schema elettrico ${subject}`,

    try_hint: 'Tocca una domanda per scriverla nel campo, senza inviarla.',
    codes_hint: 'Tocca un codice per scriverlo nel campo.',
    codes_all_title: 'Tutti i codici, per veicolo',
    codes_all_count: c => `${c} codici`,
    codes_none: 'Nessun codice guasto per questo veicolo',

    codes_group_p: 'Motore e cambio',
    codes_group_b: 'Carrozzeria',
    codes_group_c: 'Telaio, ABS e ESP',
    codes_group_u: 'Rete di bordo',
    codes_group_other: 'Altri',

    voice_title: 'Rispondere a voce',
    voice_body: 'Parla: la domanda viene trascritta e la risposta letta ad alta voce.',
    voice_hd: 'Voice HD — voce di qualità alta',
    voice_std: 'Voce — voce del sistema, più rapida',

    zoom_title: 'Schemi e manuale',
    zoom_body: 'Tocca uno schema o una pagina del manuale per vederlo a tutto schermo.',

    limits_title: 'Cosa non fa',
    limits_body: 'Risponde solo con i dati dell’archivio. Se l’informazione non c’è, lo dice — non la inventa.',

    language_title: 'Lingua',
    coverage_full: 'completo',
    coverage_no_manual: 'schemi sì, manuale no',
    coverage_data_only: 'solo dati tecnici',
  },

  en: {
    title: 'How it works',
    close: 'Close',
    placeholder: 'Describe the problem or enter a fault code...',

    demo_badge: 'DEMO',
    demo_title: 'This is a demonstration',
    demo_body: n => n === 1
      ? 'The loaded archive is deliberately limited: it holds a single vehicle, with the workshop’s real documentation.'
      : `The loaded archive is deliberately limited: it holds ${n} vehicles. For each one the documentation is the workshop’s own, and it is not the same for all of them.`,

    choose_vehicle: 'Choose the vehicle',
    choose_vehicle_hint: 'Tap a vehicle to look it up.',

    howto_title: 'How to use it',
    howto_1: 'Pick the vehicle above, or type it in the box',
    howto_2: 'Confirm the card that appears',
    howto_3: 'Ask your technical question',

    sec_cases: 'Fault cases',
    sec_photos: 'With a photo',
    sec_codes: 'Fault codes',
    sec_diagrams: 'Wiring diagrams',
    sec_manual: 'Workshop manual',
    sec_technical: 'Technical data',

    unit_cases: 'cases',
    unit_photos: 'photos',
    unit_codes: 'codes',
    unit_diagrams: 'diagrams',
    unit_manual: 'pages',
    unit_technical: 'figures',

    ask_data: subject => `Show me the technical data for ${subject}`,
    ask_diagram: subject => `Show me the ${subject} wiring diagram`,

    try_hint: 'Tap a question to put it in the box, without sending it.',
    codes_hint: 'Tap a code to put it in the box.',
    codes_all_title: 'All codes, by vehicle',
    codes_all_count: c => `${c} codes`,
    codes_none: 'No fault code for this vehicle',

    codes_group_p: 'Powertrain',
    codes_group_b: 'Body',
    codes_group_c: 'Chassis, ABS and ESP',
    codes_group_u: 'Network',
    codes_group_other: 'Other',

    voice_title: 'Answering out loud',
    voice_body: 'Speak: your question is transcribed and the answer read aloud.',
    voice_hd: 'Voice HD — high quality voice',
    voice_std: 'Voice — system voice, faster',

    zoom_title: 'Diagrams and manual',
    zoom_body: 'Tap a diagram or a manual page to see it full screen.',

    limits_title: 'What it will not do',
    limits_body: 'It answers only from the archive. If the information is not there, it says so — it does not invent it.',

    language_title: 'Language',
    coverage_full: 'complete',
    coverage_no_manual: 'diagrams yes, manual no',
    coverage_data_only: 'technical figures only',
  },

  fr: {
    title: 'Comment ça marche',
    close: 'Fermer',
    placeholder: 'Décrivez le problème ou saisissez un code défaut...',

    demo_badge: 'DÉMO',
    demo_title: 'Ceci est une démonstration',
    demo_body: n => n === 1
      ? 'L’archive chargée est volontairement limitée : elle ne contient qu’un véhicule, avec la vraie documentation d’atelier.'
      : `L’archive chargée est volontairement limitée : elle contient ${n} véhicules. Pour chacun, c’est la vraie documentation d’atelier — et elle n’est pas la même pour tous.`,

    choose_vehicle: 'Choisissez le véhicule',
    choose_vehicle_hint: 'Touchez un véhicule pour le rechercher.',

    howto_title: 'Comment l’utiliser',
    howto_1: 'Choisissez le véhicule ci-dessus, ou écrivez-le dans le champ',
    howto_2: 'Confirmez la fiche qui apparaît',
    howto_3: 'Posez votre question technique',

    sec_cases: 'Cas de panne',
    sec_photos: 'Avec photo',
    sec_codes: 'Codes défaut',
    sec_diagrams: 'Schémas électriques',
    sec_manual: 'Manuel d’atelier',
    sec_technical: 'Données techniques',

    unit_cases: 'cas',
    unit_photos: 'photos',
    unit_codes: 'codes',
    unit_diagrams: 'schémas',
    unit_manual: 'pages',
    unit_technical: 'données',

    ask_data: subject => `Montre-moi les données techniques de ${subject}`,
    ask_diagram: subject => `Montre-moi le schéma électrique ${subject}`,

    try_hint: 'Touchez une question pour l’écrire dans le champ, sans l’envoyer.',
    codes_hint: 'Touchez un code pour l’écrire dans le champ.',
    codes_all_title: 'Tous les codes, par véhicule',
    codes_all_count: c => `${c} codes`,
    codes_none: 'Aucun code défaut pour ce véhicule',

    codes_group_p: 'Moteur et boîte',
    codes_group_b: 'Carrosserie',
    codes_group_c: 'Châssis, ABS et ESP',
    codes_group_u: 'Réseau de bord',
    codes_group_other: 'Autres',

    voice_title: 'Répondre à voix haute',
    voice_body: 'Parlez : la question est transcrite et la réponse lue à voix haute.',
    voice_hd: 'Voice HD — voix de haute qualité',
    voice_std: 'Voix — voix du système, plus rapide',

    zoom_title: 'Schémas et manuel',
    zoom_body: 'Touchez un schéma ou une page du manuel pour l’afficher en plein écran.',

    limits_title: 'Ce qu’il ne fait pas',
    limits_body: 'Il répond uniquement avec les données de l’archive. Si l’information n’y est pas, il le dit — il ne l’invente pas.',

    language_title: 'Langue',
    coverage_full: 'complet',
    coverage_no_manual: 'schémas oui, manuel non',
    coverage_data_only: 'données techniques seulement',
  },

  es: {
    title: 'Cómo funciona',
    close: 'Cerrar',
    placeholder: 'Describe el problema o introduce un código de avería...',

    demo_badge: 'DEMO',
    demo_title: 'Esto es una demostración',
    demo_body: n => n === 1
      ? 'El archivo cargado está deliberadamente limitado: contiene un solo vehículo, con la documentación real del taller.'
      : `El archivo cargado está deliberadamente limitado: contiene ${n} vehículos. Para cada uno es la documentación real del taller, y no es la misma para todos.`,

    choose_vehicle: 'Elige el vehículo',
    choose_vehicle_hint: 'Toca un vehículo para buscarlo.',

    howto_title: 'Cómo se usa',
    howto_1: 'Elige el vehículo arriba, o escríbelo en el campo',
    howto_2: 'Confirma la ficha que aparece',
    howto_3: 'Haz tu pregunta técnica',

    sec_cases: 'Casos de avería',
    sec_photos: 'Con foto',
    sec_codes: 'Códigos de avería',
    sec_diagrams: 'Esquemas eléctricos',
    sec_manual: 'Manual de taller',
    sec_technical: 'Datos técnicos',

    unit_cases: 'casos',
    unit_photos: 'fotos',
    unit_codes: 'códigos',
    unit_diagrams: 'esquemas',
    unit_manual: 'páginas',
    unit_technical: 'datos',

    ask_data: subject => `Muéstrame los datos técnicos de ${subject}`,
    ask_diagram: subject => `Muéstrame el esquema eléctrico ${subject}`,

    try_hint: 'Toca una pregunta para escribirla en el campo, sin enviarla.',
    codes_hint: 'Toca un código para escribirlo en el campo.',
    codes_all_title: 'Todos los códigos, por vehículo',
    codes_all_count: c => `${c} códigos`,
    codes_none: 'Ningún código de avería para este vehículo',

    codes_group_p: 'Motor y cambio',
    codes_group_b: 'Carrocería',
    codes_group_c: 'Chasis, ABS y ESP',
    codes_group_u: 'Red de a bordo',
    codes_group_other: 'Otros',

    voice_title: 'Responder en voz alta',
    voice_body: 'Habla: la pregunta se transcribe y la respuesta se lee en voz alta.',
    voice_hd: 'Voice HD — voz de alta calidad',
    voice_std: 'Voz — voz del sistema, más rápida',

    zoom_title: 'Esquemas y manual',
    zoom_body: 'Toca un esquema o una página del manual para verlo a pantalla completa.',

    limits_title: 'Lo que no hace',
    limits_body: 'Responde solo con los datos del archivo. Si la información no está, lo dice — no la inventa.',

    language_title: 'Idioma',
    coverage_full: 'completo',
    coverage_no_manual: 'esquemas sí, manual no',
    coverage_data_only: 'solo datos técnicos',
  },

  pt: {
    title: 'Como funciona',
    close: 'Fechar',
    placeholder: 'Descreva o problema ou introduza um código de avaria...',

    demo_badge: 'DEMO',
    demo_title: 'Isto é uma demonstração',
    demo_body: n => n === 1
      ? 'O arquivo carregado é deliberadamente limitado: contém um único veículo, com a documentação real da oficina.'
      : `O arquivo carregado é deliberadamente limitado: contém ${n} veículos. Para cada um é a documentação real da oficina, e não é a mesma para todos.`,

    choose_vehicle: 'Escolha o veículo',
    choose_vehicle_hint: 'Toque num veículo para o procurar.',

    howto_title: 'Como se usa',
    howto_1: 'Escolha o veículo acima, ou escreva-o no campo',
    howto_2: 'Confirme a ficha que aparece',
    howto_3: 'Faça a sua pergunta técnica',

    sec_cases: 'Casos de avaria',
    sec_photos: 'Com foto',
    sec_codes: 'Códigos de avaria',
    sec_diagrams: 'Esquemas elétricos',
    sec_manual: 'Manual de oficina',
    sec_technical: 'Dados técnicos',

    unit_cases: 'casos',
    unit_photos: 'fotos',
    unit_codes: 'códigos',
    unit_diagrams: 'esquemas',
    unit_manual: 'páginas',
    unit_technical: 'dados',

    ask_data: subject => `Mostra-me os dados técnicos de ${subject}`,
    ask_diagram: subject => `Mostra-me o esquema elétrico ${subject}`,

    try_hint: 'Toque numa pergunta para a escrever no campo, sem a enviar.',
    codes_hint: 'Toque num código para o escrever no campo.',
    codes_all_title: 'Todos os códigos, por veículo',
    codes_all_count: c => `${c} códigos`,
    codes_none: 'Nenhum código de avaria para este veículo',

    codes_group_p: 'Motor e caixa',
    codes_group_b: 'Carroçaria',
    codes_group_c: 'Chassis, ABS e ESP',
    codes_group_u: 'Rede de bordo',
    codes_group_other: 'Outros',

    voice_title: 'Responder em voz alta',
    voice_body: 'Fale: a pergunta é transcrita e a resposta lida em voz alta.',
    voice_hd: 'Voice HD — voz de alta qualidade',
    voice_std: 'Voz — voz do sistema, mais rápida',

    zoom_title: 'Esquemas e manual',
    zoom_body: 'Toque num esquema ou numa página do manual para ver em ecrã inteiro.',

    limits_title: 'O que não faz',
    limits_body: 'Responde apenas com os dados do arquivo. Se a informação não existir, diz — não a inventa.',

    language_title: 'Idioma',
    coverage_full: 'completo',
    coverage_no_manual: 'esquemas sim, manual não',
    coverage_data_only: 'apenas dados técnicos',
  },
};

// Shown in the language menu, each in its own language - a reader looking
// for their language recognises it faster than a translation of its name
// into one they do not read.
export const LANGUAGE_NAMES: Record<HelpLanguage, string> = {
  it: 'Italiano',
  en: 'English',
  fr: 'Français',
  es: 'Español',
  pt: 'Português',
};

export function h(language: string): HelpCopy {
  return COPY[language as HelpLanguage] ?? COPY.it;
}
