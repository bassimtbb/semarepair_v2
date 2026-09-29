import { isFarewell } from './voice-farewell';
import { parseScreenCommand } from './screen-commands';

// From a real session: "Niente, grazie." was sent to the backend like a
// question, answered politely, and voice mode went back to listening on an
// empty room - ending two turns later in a transcription failure. Hands-free
// mode has no other way out from under a vehicle.

describe('isFarewell', () => {
  it('recognises the sign-off from the session, comma and all', () => {
    expect(isFarewell('Niente, grazie.', 'it')).toBe(true);
  });

  it('recognises the common sign-offs in all five languages', () => {
    expect(isFarewell('grazie mille', 'it')).toBe(true);
    expect(isFarewell('ho finito', 'it')).toBe(true);
    expect(isFarewell('a posto', 'it')).toBe(true);
    expect(isFarewell('no thanks', 'en')).toBe(true);
    expect(isFarewell("that's all", 'en')).toBe(true);
    expect(isFarewell('non merci', 'fr')).toBe(true);
    expect(isFarewell("c'est bon", 'fr')).toBe(true);
    expect(isFarewell('nada gracias', 'es')).toBe(true);
    expect(isFarewell('não obrigado', 'pt')).toBe(true);
  });

  it('tolerates the filler a spoken sign-off carries', () => {
    expect(isFarewell('ok grazie', 'it')).toBe(true);
    expect(isFarewell('va bene, grazie!', 'it')).toBe(true);
  });

  // The reason this matches the WHOLE utterance rather than any part of it.
  // Ending the session mid-repair is not a shrug, so a thank-you followed by
  // more work must never stop anything.
  it('does not stop when thanks are followed by more work', () => {
    expect(isFarewell("Grazie, ora dimmi che fusibile protegge l'ABS", 'it')).toBe(false);
    expect(isFarewell('grazie per la procedura, continuiamo', 'it')).toBe(false);
    expect(isFarewell('thanks, now show me the wiring diagram', 'en')).toBe(false);
    expect(isFarewell('merci, et le couple de serrage ?', 'fr')).toBe(false);
  });

  it('leaves ordinary questions and answers alone', () => {
    expect(isFarewell('Dove si trova il fusibile F17?', 'it')).toBe(false);
    expect(isFarewell('sì', 'it')).toBe(false);
    expect(isFarewell('due', 'it')).toBe(false);
    expect(isFarewell('no', 'it')).toBe(false); // a refusal, not a goodbye
    expect(isFarewell('Il motore non si avvia', 'it')).toBe(false);
  });

  // A bare "no" declines the pending offer and the session continues; only
  // "no grazie" ends it. The two arrive one word apart, so the distinction is
  // worth pinning.
  it('separates declining an offer from ending the session', () => {
    expect(isFarewell('no', 'it')).toBe(false);
    expect(isFarewell('no grazie', 'it')).toBe(true);
  });

  it('falls back to Italian for an unknown language', () => {
    expect(isFarewell('arrivederci', 'de')).toBe(true);
  });

  it('is not triggered by silence', () => {
    expect(isFarewell('', 'it')).toBe(false);
    expect(isFarewell('   ', 'it')).toBe(false);
  });
});

// The two local command paths share the microphone, so a word cannot belong to
// both. These run together to keep that true as either list grows.
describe('farewell and screen commands do not overlap', () => {
  it('leaves the screen words to the screen', () => {
    expect(isFarewell('esci da schermo intero', 'it')).toBe(false);
    expect(parseScreenCommand('esci da schermo intero', 'it')).toBe('shrink');
    expect(isFarewell('ingrandisci lo schema', 'it')).toBe(false);
    expect(isFarewell('riduci lo schema', 'it')).toBe(false);
  });

  it('leaves the sign-offs to the session', () => {
    expect(parseScreenCommand('Niente, grazie.', 'it')).toBeNull();
    expect(parseScreenCommand('ho finito', 'it')).toBeNull();
    expect(parseScreenCommand('arrivederci', 'it')).toBeNull();
  });
});
