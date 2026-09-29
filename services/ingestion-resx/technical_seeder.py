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

        # Insert-then-prune, NOT delete-then-insert.
        #
        # The obvious version deleted each covered (document, language) pair
        # and re-inserted it, which also threw away the embedding column: one
        # re-run destroyed 1 252 vectors, 324 seconds and the money already
        # spent on them. Since the parser gets tuned repeatedly, that cost
        # would have been paid over and over.
        #
        # So: add what is new, drop only what the folder no longer produces,
        # and leave unchanged rows - with their vectors - untouched. The
        # identity index defines "unchanged"; a chunk whose text differs in
        # any field is a different row and correctly gets re-embedded.
        pairs = sorted({(c["id_documento"], c["language"]) for c in chunks})
        values = [tuple(c.get(col) for col in COLUMNS) for c in chunks]

        with conn.cursor() as cur:
            cur.execute("SELECT count(*) FROM knowledge_chunks "
                        "WHERE (id_documento, language) IN %s", (tuple(pairs),))
            before = cur.fetchone()[0]

            cur.execute("""
                CREATE TEMP TABLE incoming_chunks
                (LIKE knowledge_chunks INCLUDING DEFAULTS) ON COMMIT DROP
            """)
            execute_values(
                cur,
                f"INSERT INTO incoming_chunks ({', '.join(COLUMNS)}) VALUES %s",
                values,
            )

            cur.execute(f"""
                INSERT INTO knowledge_chunks ({', '.join(COLUMNS)})
                SELECT {', '.join(COLUMNS)} FROM incoming_chunks
                ON CONFLICT DO NOTHING
            """)

            # asset_id is carried onto rows that already exist, and it needs
            # its own statement to get there.
            #
            # A chunk's identity is its text - heading, label, value, unit,
            # body, search_text - and asset_id is deliberately not part of it:
            # which photograph illustrates a fuse is not what makes that fuse
            # a different fuse. That is the right definition, but it means the
            # INSERT above sees an unchanged identity and does nothing, so a
            # newly delivered image would never reach the row.
            #
            # The alternative was to put asset_id in the key. It would have
            # worked, and it would have thrown away and re-embedded 1971
            # chunks whose text had not changed, for a picture.
            cur.execute("""
                UPDATE knowledge_chunks k
                SET asset_id = i.asset_id
                FROM incoming_chunks i
                WHERE k.id_documento = i.id_documento
                  AND k.language = i.language
                  AND k.kind = i.kind
                  AND COALESCE(k.heading,'')   = COALESCE(i.heading,'')
                  AND COALESCE(k.reference,'') = COALESCE(i.reference,'')
                  AND COALESCE(k.label,'')     = COALESCE(i.label,'')
                  AND COALESCE(k.value,'')     = COALESCE(i.value,'')
                  AND COALESCE(k.unit,'')      = COALESCE(i.unit,'')
                  AND COALESCE(k.body,'')      = COALESCE(i.body,'')
                  AND k.search_text = i.search_text
                  AND k.asset_id IS DISTINCT FROM i.asset_id
            """)
            relinked = cur.rowcount

            # Anything this folder no longer produces, within the pairs it
            # covers. Scoped to those pairs so a partial folder can never wipe
            # documents it says nothing about.
            cur.execute("""
                DELETE FROM knowledge_chunks k
                WHERE (k.id_documento, k.language) IN %s
                  AND NOT EXISTS (
                      SELECT 1 FROM incoming_chunks i
                      WHERE i.id_documento = k.id_documento
                        AND i.language = k.language
                        AND i.kind = k.kind
                        AND COALESCE(i.heading,'')   = COALESCE(k.heading,'')
                        AND COALESCE(i.reference,'') = COALESCE(k.reference,'')
                        AND COALESCE(i.label,'')     = COALESCE(k.label,'')
                        AND COALESCE(i.value,'')     = COALESCE(k.value,'')
                        AND COALESCE(i.unit,'')      = COALESCE(k.unit,'')
                        AND COALESCE(i.body,'')      = COALESCE(k.body,'')
                        AND i.search_text = k.search_text
                  )
            """, (tuple(pairs),))
            pruned = cur.rowcount

            cur.execute("SELECT count(*), count(embedding) FROM knowledge_chunks "
                        "WHERE (id_documento, language) IN %s", (tuple(pairs),))
            after, with_vectors = cur.fetchone()
        conn.commit()

        print(f"{len(values)} parsed -> {after} chunks stored "
              f"({after - before + pruned} added, {pruned} pruned), "
              f"{with_vectors} keep their embedding, "
              f"{relinked} re-linked to an image, "
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
