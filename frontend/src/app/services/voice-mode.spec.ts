import { buildSpokenText, findProcedure } from './voice-mode.service';
import { stripMarkdown } from '../utils/markdown';
import type { ChatResponse, TechnicalChunk } from '../models/chat.models';

// buildSpokenText and findProcedure are plain functions over a ChatResponse -
// no microphone, no speech engine, no Angular. Which makes them the only part
// of voice mode that can be checked without listening to it, and worth
// pinning: the bug these cover shipped silently because nothing else could
// have caught it.
//
// What they encode is the §7 rule the whole file rests on: the wrapper words
// are ours, the values are the database's, verbatim.

function response(over: Partial<ChatResponse> = {}): ChatResponse {
  return {
    phase: 'chat',
    found: true,
    message: 'Ecco le informazioni tecniche trovate.',
    carMatches: [],
    cases: [],
    technicalChunks: [],
    ...over,
  };
}

function chunk(over: Partial<TechnicalChunk> = {}): TechnicalChunk {
  return { idDocumento: '1', language: 'it', kind: 'fact', ...over };
}

describe('buildSpokenText — technical answers', () => {
  // The bug that started this: a technical answer spoke only the framing
  // sentence and fell silent on the answer itself - in the one mode where the
  // mechanic cannot read the screen.
  it('speaks the value, not just the framing sentence', () => {
    const spoken = buildSpokenText(response({
      technicalChunks: [chunk({ reference: 'F17', label: 'Centralina Iniezione', value: '5 (A)' })],
    }), 'it');

    expect(spoken).toContain('F17');
    expect(spoken).toContain('Centralina Iniezione');
    expect(spoken).toContain('5 (A)');
  });

  // Two fuses protect the ABS unit and both are correct; speaking only the
  // closest would hide one the mechanic still has to check.
  it('reads several short values in a row', () => {
    const spoken = buildSpokenText(response({
      technicalChunks: [
        chunk({ reference: 'F42', label: 'Centralina ABS', value: '7,5 (A)' }),
        chunk({ reference: 'F04', label: 'Centralina ABS', value: '50 (A)' }),
      ],
    }), 'it') ?? '';

    expect(spoken).toContain('F42');
    expect(spoken).toContain('F04');
  });

  it('caps the reading and says how many are left on screen', () => {
    const spoken = buildSpokenText(response({
      technicalChunks: Array.from({ length: 6 }, (_, i) =>
        chunk({ reference: `F${i}`, label: `Lampadina ${i}`, value: '12 / 55' })),
    }), 'it') ?? '';

    expect(spoken).toContain('F0');
    expect(spoken).not.toContain('F5');
    expect(spoken).toContain('3'); // the three not spoken
  });

  // A drawing cannot be read aloud. Naming it and saying where it is beats
  // both silence and a recital of eighteen reference marks.
  it('names a diagram and points at the screen', () => {
    const spoken = buildSpokenText(response({
      technicalChunks: [chunk({
        kind: 'legend', reference: 'H1', label: 'Centralina Airbag',
        heading: 'Airbag Siemens MY99', assetId: '25008449',
      })],
    }), 'it') ?? '';

    expect(spoken).toContain('Airbag Siemens MY99');
    expect(spoken).not.toContain('H1');
  });

  it('still speaks repair documents the way it always did', () => {
    const spoken = buildSpokenText(response({
      cases: [{
        idDocumento: '1', sigla: 'GUP1', titolo: 't', impianto: 'Iniezione',
        dispositivo: 'Fusibile F17', anomalia: 'a', causa: 'Fusibile bruciato',
        intervento: 'Sostituire il fusibile', procedura: '', nota: '',
        reliability: 1, language: 'it', dtcCodes: [], foundViaSharedEngine: false,
      }],
    }), 'it') ?? '';

    expect(spoken).toContain('Fusibile bruciato');
    expect(spoken).toContain('Sostituire il fusibile');
  });
});

describe('findProcedure', () => {
  // A procedure is offered, never spoken outright: eight steps is about a
  // minute, and starting that unasked is what makes a hands-free assistant
  // tiring.
  it('finds a procedure so it can be offered rather than read', () => {
    const procedure = chunk({ kind: 'section', heading: 'Regolazione - Reset', body: '• Inserire' });
    expect(findProcedure(response({ technicalChunks: [procedure] }))).toBe(procedure);
  });

  it('ignores a section with no body to read', () => {
    expect(findProcedure(response({
      technicalChunks: [chunk({ kind: 'section', heading: 'Vuota' })],
    }))).toBeNull();
  });

  it('does not gate a short value behind a question', () => {
    expect(findProcedure(response({
      technicalChunks: [chunk({ reference: 'F17', label: 'Iniezione', value: '5 (A)' })],
    }))).toBeNull();
  });
});

describe('stripMarkdown — procedure bullets', () => {
  // The bullets were added to lay steps out on screen. A synthesiser reads
  // "•" aloud or stumbles on it, so the spoken copy must lose them - and only
  // the spoken copy.
  it('removes the bullets a procedure is laid out with', () => {
    const spoken = stripMarkdown("Veicoli con meno di 200 Km\n• Inserire l'accensione\n• Premere il pulsante 1");

    expect(spoken).not.toContain('•');
    expect(spoken).toContain("Inserire l'accensione");
  });

  // The pause between steps is the line break; losing it would run the whole
  // procedure into one breath.
  it('keeps the line breaks that make it pause between steps', () => {
    expect(stripMarkdown('• Primo\n• Secondo').split('\n').length).toBe(2);
  });

  it('leaves a mid-sentence bullet character alone', () => {
    expect(stripMarkdown('coppia 2 • 3 Nm')).toBe('coppia 2 • 3 Nm');
  });
});
