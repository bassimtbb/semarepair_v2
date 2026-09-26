"""
Parses the NON-GUP resx documents into searchable chunks.

resx_parser.py handles the GUP repair sheets and is deliberately left alone -
it drives the working product. This module is the second path, for the 28
documents it cannot read, and it never touches the same blocks.

Three source shapes cover every non-GUP document that carries real content
(see docs/Architecture_Extension_v2.md section 2):

  quadruplet   XDATIMECH / XCOPSERR -> Gruppo, Dato, Valore, UnitaMis
               engine specs (DTM), torque values (COP), bulbs (FAR)

  fuse table   XFUSCONN (one per box) > XTABVAL -> Numero, Descrizione,
               Riferimento. The box title lives on the parent, so the
               nesting is what tells us WHERE a fuse sits.

  legend       XSCHLEGENDA -> Numero, NomeComp, Localizzazione. Names the
               reference marks printed on a wiring diagram (H1, S1, F01),
               which is what makes an image searchable by words.

  prose        XCAPITOLO with ANY Ordine. resx_parser only reads Ordine
               1/3/4 because that is the GUP layout; these documents use
               2, 10, 20, 50, 55, 60... and were silently dropped. This is
               15 790 characters of real technical text across 10 documents.
"""

import os
import re
from typing import Optional
import xml.etree.ElementTree as ET

TAG_RE = re.compile(r'<[^>]+>')
BR_RE = re.compile(r'<\s*BR\s*/?\s*>', re.IGNORECASE)
LI_RE = re.compile(r'<\s*LI\s*/?\s*>', re.IGNORECASE)

# Built from chr() rather than written as escapes: this file gets edited
# through shell heredocs, where a literal backslash-n has twice been
# turned into a real newline before reaching disk.
NL = chr(10)
BULLET = chr(8226) + ' '
HORIZ_WS = '[ ' + chr(9) + ']+'
PAD_AROUND_NL = ' *' + NL + ' *'
BLANK_RUN = NL + '{3,}'
# The source puts a <BR/> after every <LI>, which would leave a blank line
# between consecutive steps. A numbered procedure reads better tight.
BLANK_BEFORE_BULLET = NL + NL + chr(8226)
FILENAME_RE = re.compile(r'^(\d+)_([A-Za-z]{2})\.resx$')

# Mirrors resx_parser.FILENAME_RE's language handling, but as an allow-list:
# the FI0396 delivery also contained German and Russian, which no part of the
# product supports. Keep aligned with SystemPromptBuilder.LanguageNames.
SUPPORTED_LANGUAGES = {'it', 'en', 'fr', 'pt', 'es'}

# GUP is the repair-sheet format; resx_parser owns it entirely.
SKIPPED_TIPO_RIS = {'GUP'}

EMPTY_VALUES = {'', '- -', '0'}


def _clean(text: Optional[str]) -> Optional[str]:
    """Strips markup and collapses whitespace. Same contract as
    resx_parser._clean, duplicated rather than imported so a change made for
    the GUP path can never silently alter this one.

    For short fields - a title, a component name - flattening is right. Prose
    bodies use _clean_body instead."""
    text = TAG_RE.sub(' ', text or '')
    text = re.sub(r'\s+', ' ', text).strip()
    return text if text and text not in EMPTY_VALUES else None


def _clean_body(text: Optional[str]) -> Optional[str]:
    """Like _clean, but keeps the line structure the source actually carries.

    These chapters are procedures, and their layout is meaning: the service
    reset is eight ordered steps, not a paragraph. Flattening them the way
    _clean does produced a wall of text a mechanic cannot follow while
    standing at a vehicle - "Inserire l'accensione Premere piu volte il
    pulsante 1 fino a quando nel display 2 appare il chilometraggio totale
    Premere e mantenere premuto..."

    The markup says exactly where the breaks are: <BR/> ends a line, <LI>
    starts a step. Everything else is dropped as before. Only horizontal
    whitespace is collapsed, so a step never loses its own spacing, and runs
    of blank lines are capped at one so the source's generous <BR/><BR/>
    padding does not turn into gaps."""
    if not text:
        return None
    s = BR_RE.sub(NL, text)
    s = LI_RE.sub(NL + BULLET, s)
    s = TAG_RE.sub('', s)
    s = re.sub(HORIZ_WS, ' ', s)
    s = re.sub(PAD_AROUND_NL, NL, s)
    s = re.sub(BLANK_RUN, NL + NL, s)
    s = re.sub(BLANK_BEFORE_BULLET, NL + chr(8226), s)
    s = s.strip()
    return s if s and s not in EMPTY_VALUES else None


def _field(el: ET.Element, name: str) -> Optional[str]:
    value = (el.findtext(name) or '').strip()
    return value if value not in EMPTY_VALUES else None


def _search_text(*parts: Optional[str]) -> str:
    """The text that gets embedded. Joined with a separator rather than
    spaces so the embedding sees field boundaries instead of one run-on
    phrase."""
    return ' · '.join(p for p in parts if p)


def _quadruplets(doc: ET.Element, type_label: Optional[str]) -> list[dict]:
    """Gruppo / Dato / Valore / UnitaMis - DTM, COP, FAR."""
    chunks = []
    for tag in ('XDATIMECH', 'XCOPSERR'):
        for el in doc.findall(tag):
            label = _field(el, 'Dato')
            if not label:
                continue
            heading = _field(el, 'Gruppo')
            value = _field(el, 'Valore')
            unit = _field(el, 'UnitaMis')
            chunks.append({
                'kind': 'fact',
                'heading': heading,
                'label': label,
                'value': value,
                'unit': unit,
                'reference': None,
                'body': None,
                'asset_id': None,
                'search_text': _search_text(type_label, heading, label),
            })
    return chunks


def _fuses(doc: ET.Element, type_label: Optional[str]) -> list[dict]:
    """XFUSCONN (a fuse box) > XTABVAL (its rows). The box title is on the
    parent, and it is the useful part: "F04, 50 A" means little without
    "Scatola Fusibili - Vano Motore"."""
    chunks = []
    for box in doc.findall('XFUSCONN'):
        box_title = _field(box, 'Titolo')
        for row in box.findall('XTABVAL'):
            label = _field(row, 'Descrizione')
            reference = _field(row, 'Numero')
            if not label and not reference:
                continue
            value = _field(row, 'Riferimento')   # amperage, e.g. "50 (A)"
            chunks.append({
                'kind': 'fact',
                'heading': box_title,
                'label': label,
                'value': value,
                'unit': None,
                'reference': reference,
                'body': None,
                'asset_id': None,
                'search_text': _search_text(type_label, box_title, label, reference),
            })
    return chunks


def _legends(doc: ET.Element, doc_title: Optional[str], pdf_id: Optional[str],
             type_label: Optional[str]) -> list[dict]:
    """XSCHLEGENDA - the named components behind a diagram's reference marks.

    doc_title is prepended to the search text on purpose: the legend entry
    says "Centralina Airbag", the mechanic asks for "schema airbag", and only
    the document title carries that word."""
    chunks = []
    for el in doc.findall('XSCHLEGENDA'):
        label = _field(el, 'NomeComp')
        if not label:
            continue
        reference = _field(el, 'Numero')
        location = _field(el, 'Localizzazione')
        detail = _field(el, 'DettComp')
        chunks.append({
            'kind': 'legend',
            'heading': doc_title,
            'label': label,
            'value': location,
            'unit': None,
            'reference': reference,
            'body': detail,
            'asset_id': pdf_id,
            'search_text': _search_text(type_label, doc_title, label, location, reference),
        })
    return chunks


def _sections(doc: ET.Element, doc_title: Optional[str], type_label: Optional[str]) -> list[dict]:
    """XCAPITOLO at any Ordine. A chapter with no title of its own falls back
    to the document's, so the chunk is never anonymous in a result list."""
    chunks = []
    for cap in doc.findall('XCAPITOLO'):
        body = _clean_body(cap.findtext('Corpo'))
        if not body:
            continue
        heading = _field(cap, 'Capitolo') or doc_title
        chunks.append({
            'kind': 'section',
            'heading': heading,
            'label': None,
            'value': None,
            'unit': None,
            'reference': _field(cap, 'Ordine'),
            'body': body,
            'asset_id': None,
            # Flattened for the embedding - line breaks help a reader, not a
            # vector - so improving the body's layout does not force a
            # re-embedding of text that has not actually changed.
            'search_text': _search_text(type_label, doc_title, heading,
                                        re.sub(r'\s+', ' ', body)),
        })
    return chunks


def parse_technical_file(filepath: str) -> list[dict]:
    """Returns every chunk in one resx file. Empty list for a GUP document or
    one with no extractable content - both are normal, not errors."""
    root = ET.parse(filepath).getroot()
    doc = root.find('XDOCUMENTO')
    if doc is None:
        raise ValueError(f"No XDOCUMENTO element in {filepath}")

    tipo_ris = (doc.findtext('XTIPORIS/TipoRis') or '').strip()
    if tipo_ris in SKIPPED_TIPO_RIS:
        return []

    id_documento = (doc.findtext('ID') or '').strip()
    doc_title = _clean(doc.findtext('Titolo'))
    pdf_id = _field(doc.find('XSCHEMA'), 'RifIDFilePDF') if doc.find('XSCHEMA') is not None else None

    # DscRis is the document type spelled out, and it is localised: the same
    # SCH document reads "Schema Elettrico" in Italian and "Electrical
    # Schemes" in English, FUS reads "Fusibili e Relè" / "Fusibles et relais".
    #
    # It goes into every chunk's search text because without it those words
    # appear nowhere: a legend chunk read "Airbag Siemens MY99 · Front left
    # pretensioner · A1", so "show me the airbag wiring diagram" found
    # nothing in English while the Italian phrasing happened to land. The
    # retrieval was working by luck of the embedding rather than because the
    # text said what the document was.
    type_label = _clean(doc.findtext('XTIPORIS/DscRis'))

    chunks = (
        _quadruplets(doc, type_label)
        + _fuses(doc, type_label)
        + _legends(doc, doc_title, pdf_id, type_label)
        + _sections(doc, doc_title, type_label)
    )
    for c in chunks:
        c['id_documento'] = id_documento
        c['tipo_ris'] = tipo_ris
    return chunks


def parse_technical_folder(folder: str) -> list[dict]:
    """Every supported-language resx file in folder. Files whose name does not
    match, or whose language is not supported, are counted and reported rather
    than dropped in silence - the GUP path's habit of skipping quietly is what
    hid these documents in the first place."""
    chunks = []
    skipped_name = 0
    skipped_lang: dict[str, int] = {}
    skipped_gup = 0

    for filename in sorted(os.listdir(folder)):
        m = FILENAME_RE.match(filename)
        if not m:
            skipped_name += 1
            continue

        language = m.group(2).lower()
        if language not in SUPPORTED_LANGUAGES:
            skipped_lang[language] = skipped_lang.get(language, 0) + 1
            continue

        found = parse_technical_file(os.path.join(folder, filename))
        if not found:
            skipped_gup += 1
            continue
        for c in found:
            c['language'] = language
        chunks.extend(found)

    langs = ', '.join(f'{k}={v}' for k, v in sorted(skipped_lang.items())) or 'none'
    print(f"Parsed {len(chunks)} technical chunks "
          f"({skipped_gup} GUP/empty files skipped, "
          f"{skipped_name} malformed names, unsupported languages: {langs}).")
    return chunks
