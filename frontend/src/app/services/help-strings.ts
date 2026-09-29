export type HelpLanguage = 'it' | 'en' | 'fr' | 'pt' | 'es';

const COPY: Record<HelpLanguage, {
  title: string; heading: string; intro: string; language: string; placeholder: string;
  suggestions: string[]; close: string;
}> = {
  it: { title: 'Aiuto', heading: 'Come posso aiutarti?', intro: 'Scegli una domanda per inserirla nella chat.', language: 'Lingua predefinita', placeholder: 'Scrivi la tua domanda…', close: 'Chiudi', suggestions: ['Come trovo un guasto usando il codice DTC?', 'Mostrami i veicoli compatibili', 'Come posso ingrandire uno schema?'] },
  en: { title: 'Help', heading: 'How can I help?', intro: 'Choose a question to add it to the chat.', language: 'Default language', placeholder: 'Type your question…', close: 'Close', suggestions: ['How do I find a fault using a DTC code?', 'Show me compatible vehicles', 'How can I enlarge a wiring diagram?'] },
  fr: { title: 'Aide', heading: 'Comment puis-je vous aider ?', intro: 'Choisissez une question pour l’ajouter au chat.', language: 'Langue par défaut', placeholder: 'Saisissez votre question…', close: 'Fermer', suggestions: ['Comment rechercher une panne avec un code DTC ?', 'Afficher les véhicules compatibles', 'Comment agrandir un schéma électrique ?'] },
  pt: { title: 'Ajuda', heading: 'Como posso ajudar?', intro: 'Escolha uma pergunta para adicioná-la ao chat.', language: 'Idioma padrão', placeholder: 'Escreva a sua pergunta…', close: 'Fechar', suggestions: ['Como procuro uma avaria usando um código DTC?', 'Mostrar veículos compatíveis', 'Como posso ampliar um esquema elétrico?'] },
  es: { title: 'Ayuda', heading: '¿Cómo puedo ayudarte?', intro: 'Elige una pregunta para añadirla al chat.', language: 'Idioma predeterminado', placeholder: 'Escribe tu pregunta…', close: 'Cerrar', suggestions: ['¿Cómo busco una avería con un código DTC?', 'Muéstrame los vehículos compatibles', '¿Cómo puedo ampliar un esquema eléctrico?'] },
};

export function h(language: string) {
  return COPY[language as HelpLanguage] ?? COPY.it;
}
