// Inline markdown for assistant bubble text.
//
// The acknowledgment/framing text in ChatResponse.message is written by
// Gemini (the routing call's own wording, and BuildFormatting's "short
// natural-language note") - no prompt tells it to avoid markdown, and in
// practice it emits **bold** freely. Rendering that with plain {{ }}
// interpolation shows the asterisks verbatim, which is what this fixes.
//
// Deliberately NOT a markdown parser: no headings, links, tables or block
// lists. The prompts constrain message to a short note, and Rule 9's
// "- Quale spia si accende?" bullet block already reads correctly under
// message-bubble's white-space: pre-wrap - turning it into a <ul> would
// fight that CSS rather than help.

const ESCAPES: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
};

// Both markers must sit against non-space content, so "2 * 3 * 4" and a
// dangling asterisk stay literal. The underscore variants additionally
// require a non-word character on each outer side, which is what keeps
// identifiers like id_macchina or codice_motore intact - CommonMark draws
// the same distinction for the same reason.
const BOLD_STAR = /\*\*(?=\S)([\s\S]*?\S)\*\*/g;
const BOLD_UNDERSCORE = /(^|\W)__(?=\S)([\s\S]*?\S)__(?!\w)/g;
const ITALIC_STAR = /\*(?=\S)([^*\n]*?\S)\*/g;
const ITALIC_UNDERSCORE = /(^|\W)_(?=\S)([^_\n]*?\S)_(?!\w)/g;
const CODE = /`([^`\n]+)`/g;

/**
 * Escapes `text`, then converts inline markdown emphasis to HTML.
 *
 * The result is bound to [innerHTML] as a plain string, never through
 * bypassSecurityTrustHtml - Angular's sanitizer still runs over it, which
 * is a second layer behind the escaping done here. <strong>/<em>/<code>
 * all survive sanitization.
 */
export function renderInlineMarkdown(text: string): string {
  // Escaping runs first and once: nothing arriving from the backend can
  // become a tag, and the tags added below are never re-escaped.
  let html = text.replace(/[&<>"]/g, (c) => ESCAPES[c]);

  // Code before emphasis, so an asterisk inside `...` is left alone.
  html = html.replace(CODE, '<code>$1</code>');

  // Bold before italic: run the other way round, ** is consumed as two
  // separate * markers and comes out as nested <em>.
  html = html.replace(BOLD_STAR, '<strong>$1</strong>');
  html = html.replace(BOLD_UNDERSCORE, '$1<strong>$2</strong>');
  html = html.replace(ITALIC_STAR, '<em>$1</em>');
  html = html.replace(ITALIC_UNDERSCORE, '$1<em>$2</em>');

  return html;
}

/**
 * Removes the same markers instead of converting them, for text on its way
 * to TTS - speech engines read "**" aloud or stumble over it.
 */
export function stripMarkdown(text: string): string {
  let out = text.replace(CODE, '$1');
  out = out.replace(BOLD_STAR, '$1');
  out = out.replace(BOLD_UNDERSCORE, '$1$2');
  out = out.replace(ITALIC_STAR, '$1');
  out = out.replace(ITALIC_UNDERSCORE, '$1$2');
  // Leading block markers (#, >) on their own line - the text after them
  // is still worth speaking, the marker itself isn't.
  out = out.replace(/^[ \t]*(?:#{1,6}|>)[ \t]+/gm, '');
  return out;
}
