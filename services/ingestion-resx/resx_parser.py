"""
Parses .resx files (XML export of GUP repair documents) into structured dicts.

Field labels are translated per language ("Impianto:" / "Système :" /
"System:" / "Sistema:" / "Sistema:"), but the chapter structure is
positionally consistent across all 5 languages (IT/FR/EN/PT/ES) - verified
against real sample data:

  Ordine=1  -> reliability chapter. Capitolo title carries the star count,
               e.g. "Grado di attendibilita   (**)" -> reliability 2.
  Ordine=3  -> identification chapter, exactly 5 labelled fields in order:
               [system/impianto, device, anomaly, autodiagnosis errors, cause]
  Ordine=4  -> repair procedure chapter, exactly 3 labelled fields in order:
               [action/intervento, procedure, note]

Parsing by position (not label text) avoids needing a per-language label
dictionary. The resx text itself is correctly-encoded UTF-8 - no mojibake
fix needed here (unlike the xlsx export used by services/ingestion).
"""

import os
import re
import xml.etree.ElementTree as ET
from typing import Optional

FAULT_CODE_RE = re.compile(r'\b([PCBU]\d{4})\b')
FAULT_CODE_WITH_DESC_RE = re.compile(r'\b([PCBU]\d{4})\b\s*\(([^)]*)\)')
LABEL_SPLIT_RE = re.compile(r'<B>\s*[^<]*?:\s*</B>', re.IGNORECASE)
TAG_RE = re.compile(r'<[^>]+>')
FILENAME_RE = re.compile(r'^(\d+)_([A-Za-z]{2})\.resx$')


def _clean(text: str) -> Optional[str]:
    text = TAG_RE.sub(' ', text or '')
    text = re.sub(r'\s+', ' ', text).strip()
    if not text or text == '- -':
        return None
    return text


def _split_labelled_segments(corpo: Optional[str]) -> list[str]:
    """Splits a chapter's Corpo text into the text following each <B>...:</B>
    label, in order. The text before the first label is discarded."""
    if not corpo:
        return []
    parts = LABEL_SPLIT_RE.split(corpo)
    return [_clean(p) for p in parts[1:]]


def _parse_reliability(capitolo_title: Optional[str]) -> Optional[int]:
    if not capitolo_title:
        return None
    stars = capitolo_title.count('*')
    return stars or None


def _parse_fault_codes(info_doc: Optional[str]) -> list[str]:
    if not info_doc:
        return []
    seen = []
    for code in FAULT_CODE_RE.findall(info_doc):
        if code not in seen:
            seen.append(code)
    return seen


def _parse_fault_code_descriptions(errors_text: Optional[str]) -> dict[str, str]:
    if not errors_text:
        return {}
    return {code: desc.strip() for code, desc in FAULT_CODE_WITH_DESC_RE.findall(errors_text)}


def parse_resx_file(filepath: str) -> dict:
    root = ET.parse(filepath).getroot()
    doc = root.find('XDOCUMENTO')
    if doc is None:
        raise ValueError(f"No XDOCUMENTO element in {filepath}")

    id_documento = (doc.findtext('ID') or '').strip()
    sigla_documento = (doc.findtext('DOCNumber') or '').strip() or None
    titolo = _clean(doc.findtext('Titolo'))
    tipo_ris = (doc.findtext('XTIPORIS/TipoRis') or '').strip() or None
    fault_codes = _parse_fault_codes(doc.findtext('InfoDoc'))

    reliability = None
    impianto = dispositivo = anomalia = causa = None
    intervento = procedura = nota = None
    fault_code_descriptions: dict[str, str] = {}

    for cap in doc.findall('XCAPITOLO'):
        ordine = (cap.findtext('Ordine') or '').strip()
        corpo = cap.findtext('Corpo')

        if ordine == '1':
            reliability = _parse_reliability(cap.findtext('Capitolo'))
        elif ordine == '3':
            segs = _split_labelled_segments(corpo)
            segs += [None] * (5 - len(segs))
            impianto, dispositivo, anomalia, errors_text, causa = segs[:5]
            fault_code_descriptions = _parse_fault_code_descriptions(errors_text)
        elif ordine == '4':
            segs = _split_labelled_segments(corpo)
            segs += [None] * (3 - len(segs))
            intervento, procedura, nota = segs[:3]

    return {
        "id_documento": id_documento,
        "sigla_documento": sigla_documento,
        "tipo_ris": tipo_ris,
        "titolo": titolo,
        "impianto": impianto,
        "dispositivo": dispositivo,
        "anomalia": anomalia,
        "causa": causa,
        "intervento": intervento,
        "procedura": procedura,
        "nota": nota,
        "reliability": reliability,
        "fault_codes": fault_codes,
        "fault_code_descriptions": fault_code_descriptions,
    }


def parse_resx_folder(folder: str) -> list[dict]:
    """Parses every {id_documento}_{LANG}.resx file in folder.
    Returns one dict per file, with 'language' (lowercase) attached."""
    results = []
    skipped = 0

    for filename in sorted(os.listdir(folder)):
        m = FILENAME_RE.match(filename)
        if not m:
            skipped += 1
            continue

        filepath = os.path.join(folder, filename)
        record = parse_resx_file(filepath)
        record["language"] = m.group(2).lower()

        if record["id_documento"] != m.group(1):
            print(f"WARNING: {filename} filename id {m.group(1)} != "
                  f"XML <ID> {record['id_documento']}")

        results.append(record)

    print(f"Parsed {len(results)} resx files ({skipped} skipped).")
    return results
