// Text for the help drawer, in the five languages the product speaks.
//
// Deliberately NOT in voice-strings.ts. That file's contract, stated in its
// first three lines, is that every string there wraps a raw archive value
// and is safe to read aloud. This text describes the interface instead - it
// is written by us, about us - so it lives beside it rather than inside it.
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

  // The demo notice. First thing in the drawer and first thing the client
  // reads: the archive holds one vehicle, on purpose. Said plainly, it
  // reads as a scope; left unsaid, the first question about another car
  // reads as the product being broken.
  demo_badge: string;
  demo_title: string;
  demo_body: string;
  demo_vehicle: (vehicle: string) => string;
  demo_counts: (facts: number, diagrams: number, pages: number) => string;
  demo_counts_no_manual: (facts: number, diagrams: number) => string;
  demo_counts_data_only: (facts: number) => string;
  demo_repairs: (n: number) => string;

  howto_title: string;
  howto_1: string;
  howto_2: string;
  howto_3: string;

  try_title: string;
  try_hint: string;

  // The fault codes the archive can answer. A tester who types a code at
  // random gets a correct "not found" and concludes the product is broken -
  // which is exactly what happened the first time a client tried it.
  codes_title: string;
  codes_count: (n: number) => string;
  codes_hint: string;
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

  // Typed INTO the chat, so these are questions in the reader's language,
  // not labels describing questions. All four were measured against the
  // live archive and return something to look at.
  q_fuse: string;
  q_fusebox: string;
  q_diagram: string;
  q_airbag: string;
}

const COPY: Record<HelpLanguage, HelpCopy> = {
  it: {
    title: 'Come funziona',
    close: 'Chiudi',
    placeholder: 'Descrivi il problema o inserisci un codice guasto...',

    demo_badge: 'DEMO',
    demo_title: 'Questa è una dimostrazione',
    demo_body: 'L’archivio caricato è volutamente limitato: contiene un solo veicolo. Per quel veicolo però la documentazione è completa, ed è quella reale dell’officina.',
    demo_vehicle: v => `Veicolo disponibile: ${v}`,
    demo_counts: (f, d, p) => `${f} dati tecnici · ${d} schemi elettrici · ${p} pagine di manuale d’officina`,
    demo_counts_no_manual: (f, d) => `${f} dati tecnici · ${d} schemi elettrici`,
    demo_counts_data_only: f => `${f} dati tecnici`,
    demo_repairs: n => `${n} schede di riparazione`,

    howto_title: 'Come si usa',
    howto_1: 'Indica il veicolo, per esempio «Fiat 500 1.2 benzina»',
    howto_2: 'Scegli la scheda del veicolo che compare',
    howto_3: 'Fai la tua domanda tecnica',

    try_title: 'Prova queste domande',
    try_hint: 'Tocca una domanda per scriverla nel campo, senza inviarla.',

    codes_title: 'Codici guasto disponibili',
    codes_count: n => `${n} codici nell’archivio`,
    codes_hint: 'Tocca un codice per scriverlo nel campo.',
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

    q_fuse: 'Quale fusibile protegge la centralina ABS',
    q_fusebox: 'Dove si trova la scatola dei fusibili',
    q_diagram: 'Mostrami lo schema elettrico dell’iniezione',
    q_airbag: 'Schema elettrico airbag',
  },

  en: {
    title: 'How it works',
    close: 'Close',
    placeholder: 'Describe the problem or enter a fault code...',

    demo_badge: 'DEMO',
    demo_title: 'This is a demonstration',
    demo_body: 'The loaded archive is deliberately limited: it holds a single vehicle. For that vehicle the documentation is complete, and it is the workshop’s real documentation.',
    demo_vehicle: v => `Vehicle available: ${v}`,
    demo_counts: (f, d, p) => `${f} technical figures · ${d} wiring diagrams · ${p} workshop manual pages`,
    demo_counts_no_manual: (f, d) => `${f} technical figures · ${d} wiring diagrams`,
    demo_counts_data_only: f => `${f} technical figures`,
    demo_repairs: n => `${n} repair sheets`,

    howto_title: 'How to use it',
    howto_1: 'Name the vehicle, for example "Fiat 500 1.2 petrol"',
    howto_2: 'Pick the vehicle card that appears',
    howto_3: 'Ask your technical question',

    try_title: 'Try these questions',
    try_hint: 'Tap a question to put it in the box, without sending it.',

    codes_title: 'Fault codes available',
    codes_count: n => `${n} codes in the archive`,
    codes_hint: 'Tap a code to put it in the box.',
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

    q_fuse: 'Which fuse protects the ABS control unit',
    q_fusebox: 'Where is the fuse box located',
    q_diagram: 'Show me the injection wiring diagram',
    q_airbag: 'Airbag wiring diagram',
  },

  fr: {
    title: 'Comment ça marche',
    close: 'Fermer',
    placeholder: 'Décrivez le problème ou saisissez un code défaut...',

    demo_badge: 'DÉMO',
    demo_title: 'Ceci est une démonstration',
    demo_body: 'L’archive chargée est volontairement limitée : elle ne contient qu’un seul véhicule. Pour ce véhicule, en revanche, la documentation est complète — c’est la vraie documentation d’atelier.',
    demo_vehicle: v => `Véhicule disponible : ${v}`,
    demo_counts: (f, d, p) => `${f} données techniques · ${d} schémas électriques · ${p} pages de manuel d’atelier`,
    demo_counts_no_manual: (f, d) => `${f} données techniques · ${d} schémas électriques`,
    demo_counts_data_only: f => `${f} données techniques`,
    demo_repairs: n => `${n} fiches de réparation`,

    howto_title: 'Comment l’utiliser',
    howto_1: 'Indiquez le véhicule, par exemple « Fiat 500 1.2 essence »',
    howto_2: 'Choisissez la fiche véhicule qui apparaît',
    howto_3: 'Posez votre question technique',

    try_title: 'Essayez ces questions',
    try_hint: 'Touchez une question pour l’écrire dans le champ, sans l’envoyer.',

    codes_title: 'Codes défaut disponibles',
    codes_count: n => `${n} codes dans l’archive`,
    codes_hint: 'Touchez un code pour l’écrire dans le champ.',
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

    q_fuse: 'Quel fusible protège le calculateur ABS',
    q_fusebox: 'Où se trouve la boîte à fusibles',
    q_diagram: 'Montre-moi le schéma électrique de l’injection',
    q_airbag: 'Schéma électrique airbag',
  },

  es: {
    title: 'Cómo funciona',
    close: 'Cerrar',
    placeholder: 'Describe el problema o introduce un código de avería...',

    demo_badge: 'DEMO',
    demo_title: 'Esto es una demostración',
    demo_body: 'El archivo cargado está deliberadamente limitado: contiene un solo vehículo. Para ese vehículo, en cambio, la documentación está completa, y es la documentación real del taller.',
    demo_vehicle: v => `Vehículo disponible: ${v}`,
    demo_counts: (f, d, p) => `${f} datos técnicos · ${d} esquemas eléctricos · ${p} páginas de manual de taller`,
    demo_counts_no_manual: (f, d) => `${f} datos técnicos · ${d} esquemas eléctricos`,
    demo_counts_data_only: f => `${f} datos técnicos`,
    demo_repairs: n => `${n} fichas de reparación`,

    howto_title: 'Cómo se usa',
    howto_1: 'Indica el vehículo, por ejemplo «Fiat 500 1.2 gasolina»',
    howto_2: 'Elige la ficha del vehículo que aparece',
    howto_3: 'Haz tu pregunta técnica',

    try_title: 'Prueba estas preguntas',
    try_hint: 'Toca una pregunta para escribirla en el campo, sin enviarla.',

    codes_title: 'Códigos de avería disponibles',
    codes_count: n => `${n} códigos en el archivo`,
    codes_hint: 'Toca un código para escribirlo en el campo.',
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

    q_fuse: 'Qué fusible protege la centralita ABS',
    q_fusebox: 'Dónde está la caja de fusibles',
    q_diagram: 'Muéstrame el esquema eléctrico de la inyección',
    q_airbag: 'Esquema eléctrico airbag',
  },

  pt: {
    title: 'Como funciona',
    close: 'Fechar',
    placeholder: 'Descreva o problema ou introduza um código de avaria...',

    demo_badge: 'DEMO',
    demo_title: 'Isto é uma demonstração',
    demo_body: 'O arquivo carregado é deliberadamente limitado: contém um único veículo. Para esse veículo, porém, a documentação está completa, e é a documentação real da oficina.',
    demo_vehicle: v => `Veículo disponível: ${v}`,
    demo_counts: (f, d, p) => `${f} dados técnicos · ${d} esquemas elétricos · ${p} páginas de manual de oficina`,
    demo_counts_no_manual: (f, d) => `${f} dados técnicos · ${d} esquemas elétricos`,
    demo_counts_data_only: f => `${f} dados técnicos`,
    demo_repairs: n => `${n} fichas de reparação`,

    howto_title: 'Como se usa',
    howto_1: 'Indique o veículo, por exemplo «Fiat 500 1.2 gasolina»',
    howto_2: 'Escolha a ficha do veículo que aparece',
    howto_3: 'Faça a sua pergunta técnica',

    try_title: 'Experimente estas perguntas',
    try_hint: 'Toque numa pergunta para a escrever no campo, sem a enviar.',

    codes_title: 'Códigos de avaria disponíveis',
    codes_count: n => `${n} códigos no arquivo`,
    codes_hint: 'Toque num código para o escrever no campo.',
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

    q_fuse: 'Que fusível protege a centralina ABS',
    q_fusebox: 'Onde fica a caixa de fusíveis',
    q_diagram: 'Mostra-me o esquema elétrico da injeção',
    q_airbag: 'Esquema elétrico airbag',
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
