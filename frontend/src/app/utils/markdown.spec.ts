import { renderInlineMarkdown, stripMarkdown } from './markdown';

describe('renderInlineMarkdown', () => {
  it('bolds the confirmation text Gemini actually emits', () => {
    const src = 'Perfetto, veicolo confermato: **FIAT Ducato 2.3 Multijet (120 CV) - Motore F1AE0481D**.';
    expect(renderInlineMarkdown(src)).toBe(
      'Perfetto, veicolo confermato: <strong>FIAT Ducato 2.3 Multijet (120 CV) - Motore F1AE0481D</strong>.',
    );
  });

  it('escapes HTML before adding any tags of its own', () => {
    expect(renderInlineMarkdown('<script>alert("x")</script>')).toBe(
      '&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;',
    );
  });

  it('does not let escaped markup be reopened by emphasis markers', () => {
    expect(renderInlineMarkdown('**<b>hi</b>**')).toBe('<strong>&lt;b&gt;hi&lt;/b&gt;</strong>');
  });

  it('renders ** as one <strong>, not nested <em>', () => {
    const html = renderInlineMarkdown('**forte**');
    expect(html).toBe('<strong>forte</strong>');
    expect(html).not.toContain('<em>');
  });

  it('renders single-asterisk italic', () => {
    expect(renderInlineMarkdown('un *forse* qui')).toBe('un <em>forse</em> qui');
  });

  it('leaves a standalone asterisk and spaced arithmetic alone', () => {
    expect(renderInlineMarkdown('coppia 2 * 3 Nm *')).toBe('coppia 2 * 3 Nm *');
  });

  it('leaves underscores inside identifiers alone', () => {
    expect(renderInlineMarkdown('campo id_macchina e codice_motore')).toBe(
      'campo id_macchina e codice_motore',
    );
  });

  it('renders inline code and keeps an asterisk inside it literal', () => {
    expect(renderInlineMarkdown('usa `a * b` qui')).toBe('usa <code>a * b</code> qui');
  });

  it('leaves newlines and bullet lines untouched for pre-wrap', () => {
    const src = 'Puoi descrivere meglio?\n- Quale spia si accende?\n- Quando si manifesta?';
    expect(renderInlineMarkdown(src)).toBe(src);
  });
});

describe('stripMarkdown', () => {
  it('drops bold markers from the confirmation text', () => {
    expect(stripMarkdown('veicolo confermato: **FIAT Ducato** ora')).toBe(
      'veicolo confermato: FIAT Ducato ora',
    );
  });

  it('drops italic, code and heading markers', () => {
    expect(stripMarkdown('## Nota\nun *forse* e `P0504`')).toBe('Nota\nun forse e P0504');
  });

  it('leaves text with no markdown unchanged', () => {
    const src = 'Non ho trovato documenti per questo veicolo.';
    expect(stripMarkdown(src)).toBe(src);
  });

  it('leaves identifiers and arithmetic alone', () => {
    expect(stripMarkdown('id_macchina, coppia 2 * 3 Nm')).toBe('id_macchina, coppia 2 * 3 Nm');
  });
});
