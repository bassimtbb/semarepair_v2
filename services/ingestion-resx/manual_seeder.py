"""
Loads the scanned workshop manual into knowledge_chunks, one chunk per page.

The manual is "Magneti Marelli IAW RL 5SF8 su Fiat 500" (Rivista Tecnica
Elettronica 112, 2008), delivered as an 89-page scan with no text layer.
services/ocr-manual produced Data/MANUAL/ocr.json from it.

Why the text is indexed but never displayed
-------------------------------------------
Every other chunk in this table carries text the client's archive wrote.
These carry text a machine guessed at, from photographs of paper. The two
cannot be trusted the same way, so they are not used the same way: the OCR
text is embedded so a page can be FOUND, and what the mechanic is shown is
the page image itself (asset_id -> /assets/manual/<id>).

That division is what makes OCR acceptable here at all. A misread character
can only surface the wrong page - visible at a glance, and recoverable -
never a wrong torque or amperage on screen, because no OCR character is ever
rendered as an answer.

kind='manual' rather than 'section' for the same reason: the card marks it as
a scan, and voice mode refuses to read its numbers aloud.
"""

import json
import os
import re
import sys

import psycopg2
from psycopg2.extras import execute_values

OUR_DB = os.environ["OUR_DB"]
MANUAL_PATH = os.environ.get("MANUAL_PATH", "/app/Data/MANUAL")

ID_DOCUMENTO = "122"
LANGUAGE = "it"          # the scan is Italian only; no translation exists
TITLE = "Magneti Marelli IAW RL 5SF8 su Fiat 500 - Rivista Tecnica 112"

COLUMNS = [
    "id_documento", "language", "kind", "heading", "label",
    "value", "unit", "reference", "body", "search_text", "asset_id",
]

# Lines that are furniture, not content: the running header on every page, the
# publisher's watermark, and a bare page number.
FURNITURE = re.compile(r"^(fiat\s*500|w{2,}\s*\.?\s*semantica\s*\.?\s*it|\d{1,3})$", re.I)


def is_title(line: str) -> bool:
    """A section title is set in capitals in this manual."""
    letters = [c for c in line if c.isalpha()]
    if len(letters) < 6:
        return False
    upper = sum(1 for c in letters if c.isupper()) / len(letters)
    return upper >= 0.7 and len(line) <= 90


def page_heading(text: str, carried: str | None) -> str | None:
    """
    The section this page belongs to.

    A page that opens with a capitalised title starts a section. A page that
    opens mid-sentence is a continuation, and belongs to the last title seen -
    the manual is read in order, so carrying it forward is what a reader does
    anyway. Without this, continuation pages would be indexed with no subject
    at all and become unfindable by topic.
    """
    for raw in text.split("\n"):
        line = raw.strip()
        if not line or FURNITURE.match(line):
            continue
        return line if is_title(line) else carried
    return carried


def main() -> None:
    src = os.path.join(MANUAL_PATH, "ocr.json")
    if not os.path.exists(src):
        print(f"ERROR: {src} not found - run services/ocr-manual first", file=sys.stderr)
        sys.exit(1)

    pages = json.load(open(src, encoding="utf-8"))
    rows, carried, skipped = [], None, 0

    for asset_id in sorted(pages):
        body = pages[asset_id].strip()
        heading = page_heading(body, carried)
        if heading:
            carried = heading

        # A page carrying almost no text is a full-page photograph. Indexing it
        # would add a chunk that matches nothing and can only dilute the search.
        if len(body) < 120:
            skipped += 1
            continue

        # The page number, and it is load-bearing rather than decorative.
        #
        # A section runs over several pages and each of them carries the same
        # carried-forward title, so two cards for "SCATOLA DERIVAZIONE
        # FUSIBILI-RELE ALIMENTAZIONE" read as one page printed twice -
        # observed live on pages 21 and 22. They hold different content; only
        # the label was identical. SISTEMA ACCENSIONE spans ten such pages, so
        # this is the common case rather than an edge one.
        page = str(int(asset_id[len(ID_DOCUMENTO):]))

        search_text = f"{heading}\n{body}" if heading else body
        rows.append((ID_DOCUMENTO, LANGUAGE, "manual", heading, None,
                     None, None, page, body, search_text, asset_id))

    conn = psycopg2.connect(OUR_DB)
    try:
        with conn, conn.cursor() as cur:
            # The manual is not in the client's catalogue - it is a magazine,
            # not one of their .resx documents - so its documents row is
            # written here. tipo_ris 'MAN' keeps it out of the fault-sheet
            # paths, which select on fault content, not on this code.
            cur.execute(
                "INSERT INTO documents (id_documento, language, sigla_documento, tipo_ris, titolo) "
                "VALUES (%s, %s, %s, %s, %s) "
                "ON CONFLICT (id_documento, language) DO UPDATE SET titolo = EXCLUDED.titolo",
                (ID_DOCUMENTO, LANGUAGE, "RT112", "MAN", TITLE),
            )

            cur.execute(
                "DELETE FROM knowledge_chunks WHERE id_documento = %s AND language = %s",
                (ID_DOCUMENTO, LANGUAGE),
            )
            execute_values(
                cur,
                f"INSERT INTO knowledge_chunks ({', '.join(COLUMNS)}) VALUES %s",
                rows,
            )
            cur.execute(
                "SELECT count(*) FROM knowledge_chunks WHERE id_documento = %s AND language = %s",
                (ID_DOCUMENTO, LANGUAGE),
            )
            total = cur.fetchone()[0]
    finally:
        conn.close()

    titled = sum(1 for r in rows if r[3])
    print(f"{len(pages)} pages read, {skipped} skipped as image-only")
    print(f"{total} chunks written for document {ID_DOCUMENTO} ({titled} with a section title)")
    print("Embeddings are NOT generated here - run technical_embedder.py next.")


if __name__ == "__main__":
    main()
