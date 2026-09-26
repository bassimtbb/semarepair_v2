"""
Loads knowledge_chunks from the non-GUP resx documents.

Standalone on purpose. seeder.py drives the working product and is not
touched: this script can be run, re-run or never run at all without
affecting it. Nothing here writes to documents, gup_rows, graph_edges or
either embeddings table.

Day 1 of docs/Architecture_Extension_v2.md: extraction only. Embeddings are
generated separately (day 2), so this script needs no Gemini key and costs
nothing to run.

Re-running is safe: a document's chunks are deleted before its new ones are
inserted, so the table converges on the folder's content instead of
accumulating duplicates. That matters because the parser will be tuned
several times before the shape is right.

    docker compose run --rm --entrypoint python ingestion-resx technical_seeder.py
"""

import os
import sys
import psycopg2
from psycopg2.extras import execute_values

from technical_parser import parse_technical_folder

DATABASE_URL = os.environ.get(
    "OUR_DB", "postgresql://semarepair:semarepair@localhost:5432/semarepair")
RESX_PATH = os.environ.get(
    "RESX_PATH", os.path.join(os.path.dirname(__file__), "..", "..", "Data", "resx_samples"))

COLUMNS = [
    "id_documento", "language", "kind", "heading", "label",
    "value", "unit", "reference", "body", "search_text", "asset_id",
]


def main():
    conn = psycopg2.connect(DATABASE_URL)
    try:
        with open(os.path.join(os.path.dirname(__file__), "schema_technical.sql")) as f:
            schema_sql = f.read()
        with conn.cursor() as cur:
            cur.execute(schema_sql)
        conn.commit()

        chunks = parse_technical_folder(RESX_PATH)
        if not chunks:
            print("No technical chunks parsed - nothing to load.")
            return

        # Replace per (document, language) rather than truncating the table:
        # a partial folder must never silently wipe chunks it does not cover.
        pairs = sorted({(c["id_documento"], c["language"]) for c in chunks})
        with conn.cursor() as cur:
            cur.execute(
                "DELETE FROM knowledge_chunks WHERE (id_documento, language) IN %s",
                (tuple(pairs),),
            )
            deleted = cur.rowcount

            values = [tuple(c.get(col) for col in COLUMNS) for c in chunks]
            execute_values(
                cur,
                f"INSERT INTO knowledge_chunks ({', '.join(COLUMNS)}) VALUES %s "
                f"ON CONFLICT DO NOTHING",
                values,
            )
            # execute_values sends the rows in pages, so cur.rowcount holds
            # the last page's count, not the total - it read "52 inserted" on
            # a 1252-row load. Count the rows back instead.
            cur.execute(
                "SELECT count(*) FROM knowledge_chunks WHERE (id_documento, language) IN %s",
                (tuple(pairs),),
            )
            inserted = cur.fetchone()[0]
        conn.commit()

        print(f"Replaced {deleted} existing chunks with {inserted} new ones "
              f"({len(values)} parsed, {len(values) - inserted} duplicates dropped) "
              f"across {len(pairs)} document/language pairs.")

        with conn.cursor() as cur:
            cur.execute("""
                SELECT kind, language, count(*)
                FROM knowledge_chunks GROUP BY 1, 2 ORDER BY 1, 2
            """)
            for kind, lang, n in cur.fetchall():
                print(f"  {kind:<8} {lang}  {n:>5}")
    finally:
        conn.close()


if __name__ == "__main__":
    main()
