import { parseScreenCommand } from './screen-commands';
import { SchemaFocusService, SchemaFocusTarget } from './schema-focus.service';

// The command that started this, from a real transcript: "Poi ingrandisci
// questo schema" reached the model, which apologised for having no graphical
// ability and advised pressing Ctrl + "+" - while the enlarge control sat in
// the diagram's own title bar.
//
// parseScreenCommand and SchemaFocusService are plain code over strings and a
// registry: no microphone, no speech engine, no PDF. Which makes them the part
// of this feature that can be checked without listening to it or looking at it.

describe('parseScreenCommand', () => {
  it('recognises the phrasing from the transcript, filler and all', () => {
    expect(parseScreenCommand('Poi ingrandisci questo schema', 'it')).toBe('enlarge');
  });

  // Speech arrives without punctuation and mid-sentence, so anchoring to the
  // start of the utterance would miss most of what a mechanic actually says.
  it('matches mid-sentence and ignores punctuation', () => {
    expect(parseScreenCommand('senti, mettilo a tutto schermo!', 'it')).toBe('enlarge');
  });

  // The ordering trap this file exists to pin: "esci da schermo intero"
  // contains "schermo intero", so testing enlarge first reads an exit as an
  // enter - and the mechanic, who cannot see the screen, gets the opposite of
  // what he asked for.
  it('reads an exit as an exit, not as an enter', () => {
    expect(parseScreenCommand('esci da schermo intero', 'it')).toBe('shrink');
    expect(parseScreenCommand('exit full screen', 'en')).toBe('shrink');
    expect(parseScreenCommand('quitte le plein écran', 'fr')).toBe('shrink');
  });

  it('works in all five languages', () => {
    expect(parseScreenCommand('ingrandisci lo schema', 'it')).toBe('enlarge');
    expect(parseScreenCommand('make it full screen', 'en')).toBe('enlarge');
    expect(parseScreenCommand('agrandis le schéma', 'fr')).toBe('enlarge');
    expect(parseScreenCommand('amplía el esquema', 'es')).toBe('enlarge');
    expect(parseScreenCommand('amplia o esquema', 'pt')).toBe('enlarge');
  });

  // A false positive here would swallow a real question, which costs more than
  // a missed command: the mechanic can always press the button.
  it('leaves a technical question alone', () => {
    expect(parseScreenCommand('Dove si trova il fusibile F17?', 'it')).toBeNull();
    expect(parseScreenCommand("Il motore non si avvia dopo un arresto in marcia", 'it')).toBeNull();
    expect(parseScreenCommand('Mostrami lo schema elettrico dell’airbag', 'it')).toBeNull();
    expect(parseScreenCommand('sì', 'it')).toBeNull();
    expect(parseScreenCommand('due', 'it')).toBeNull();
  });

  it('falls back to Italian for an unknown language', () => {
    expect(parseScreenCommand('ingrandisci', 'de')).toBe('enlarge');
  });
});

describe('SchemaFocusService', () => {
  function target(title: string): SchemaFocusTarget & { fullscreen: boolean } {
    return {
      schemaTitle: title,
      fullscreen: false,
      isFullscreen() { return this.fullscreen; },
      enterFullscreen() { this.fullscreen = true; },
      exitFullscreen() { this.fullscreen = false; },
    };
  }

  it('has nothing to act on before a diagram is shown', () => {
    const service = new SchemaFocusService();
    expect(service.hasSchema()).toBe(false);
    expect(service.enlarge()).toBeNull();
  });

  // "Questo schema" means the one furthest down the conversation, not the
  // first one ever shown.
  it('acts on the most recent diagram when several are open', () => {
    const service = new SchemaFocusService();
    const first = target('Airbag Siemens MY99');
    const second = target('ABS Bosch 5.3');
    service.register(first);
    service.register(second);

    expect(service.enlarge()).toBe('ABS Bosch 5.3');
    expect(second.fullscreen).toBe(true);
    expect(first.fullscreen).toBe(false);
  });

  // Said twice, because the mechanic cannot see whether the first one worked.
  // A toggle would shrink it again - which is why the component exposes enter
  // and exit rather than only toggleFullscreen().
  it('stays enlarged when the command is repeated', () => {
    const service = new SchemaFocusService();
    const only = target('Airbag Siemens MY99');
    service.register(only);

    service.enlarge();
    service.enlarge();

    expect(only.fullscreen).toBe(true);
  });

  it('falls back to the previous diagram once one is destroyed', () => {
    const service = new SchemaFocusService();
    const first = target('Airbag Siemens MY99');
    const second = target('ABS Bosch 5.3');
    service.register(first);
    service.register(second);
    service.unregister(second);

    expect(service.enlarge()).toBe('Airbag Siemens MY99');
  });
});
