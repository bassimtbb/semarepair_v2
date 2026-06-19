"""
Excel parser for GUP_PER_IA.xlsx.

Extracts the vehicle <-> document mapping only (id_macchina + vehicle
attributes, id_documento). Document content (title, chapters, fault
codes) is parsed separately, directly from the resx files.

The first 11 CSV fields per row (id_macchina ... id_documento) are
always intact in the first Excel column - only fields after that
(titolo_documento onward) can be split across extra columns when the
title text itself contains a comma. Since we only read up to field 10,
no column-joining is needed.
"""

import csv
import io
from typing import Optional
import openpyxl


def _safe_int(value: str) -> Optional[int]:
    """Converts a string to int. Returns None if empty or not numeric."""
    v = value.strip()
    if not v:
        return None
    try:
        return int(float(v))
    except (ValueError, TypeError):
        return None


def parse_excel(filepath: str) -> list[dict]:
    """
    Reads the Excel file and returns one row dict per unique
    (id_macchina, id_documento) pair. Skips rows with missing
    id_macchina or id_documento, and duplicate pairs (the source data
    repeats the same vehicle/document pair once per document chapter).
    """
    wb = openpyxl.load_workbook(filepath, read_only=True, data_only=True)
    ws = wb.active

    rows = []
    seen = set()
    skipped = 0
    duplicates = 0

    for excel_row in ws.iter_rows(min_row=2, values_only=True):
        first_cell = excel_row[0]
        if first_cell is None:
            continue

        for fields in csv.reader(io.StringIO(str(first_cell))):
            if len(fields) < 11:
                skipped += 1
                continue

            id_macchina = fields[0].strip()
            id_documento = fields[10].strip()

            if not id_macchina or not id_documento:
                skipped += 1
                continue

            key = (id_macchina, id_documento)
            if key in seen:
                duplicates += 1
                continue
            seen.add(key)

            rows.append({
                "id_macchina":             id_macchina,
                "marca_macchina":          fields[1].strip(),
                "modello_macchina":        fields[2].strip(),
                "anno_inizio_macchina":    _safe_int(fields[3]),
                "anno_fine_macchina":      _safe_int(fields[4]),
                "alimentazione_macchina":  fields[5].strip() or None,
                "motorizzazione_macchina": fields[6].strip() or None,
                "kw_macchina":             _safe_int(fields[7]),
                "cavalli_macchina":        _safe_int(fields[8]),
                "codice_motore_macchina":  fields[9].strip(),
                "id_documento":            id_documento,
            })

    wb.close()

    print(f"Parsed {len(rows)} unique vehicle-document rows "
          f"({duplicates} duplicates, {skipped} skipped).")

    from collections import Counter
    for brand, count in sorted(Counter(r["marca_macchina"] for r in rows).items()):
        print(f"  {brand:<12} {count:>4} rows")

    return rows
