"""
Generates the embedding for every knowledge_chunks row that lacks one.

Day 2 of docs/Architecture_Extension_v2.md. Reuses embedder.Embedder
unchanged - same model, same 768 dimensions, same retry policy, same usage
accounting - so this path cannot drift from the one that embeds repair
sheets.

Resumable by construction: it only selects rows WHERE embedding IS NULL.
A run cut short by a network failure or a container restart is continued by
running it again, and nothing already paid for is paid for twice. That
matters at ~1 250 sequential API calls.

    docker compose run --rm --entrypoint python ingestion-resx technical_embedder.py
"""

import os
import sys
import time
import psycopg2
from pgvector.psycopg2 import register_vector

from embedder import Embedder
from seeder import log_usage

DATABASE_URL = os.environ.get(
    "OUR_DB", "postgresql://semarepair:semarepair@localhost:5432/semarepair")
GEMINI_API_KEY = os.environ.get("GEMINI_API_KEY", "")

# Small enough that a crash loses little work, large enough that the commit
# overhead stays invisible next to the API latency.
COMMIT_EVERY = 25


def main():
    if not GEMINI_API_KEY:
        print("GEMINI_API_KEY not set - nothing to do.")
        sys.exit(1)

    started = time.time()
    conn = psycopg2.connect(DATABASE_URL)
    register_vector(conn)
    try:
        with conn.cursor() as cur:
            cur.execute("SELECT count(*) FROM knowledge_chunks")
            total = cur.fetchone()[0]
            cur.execute("SELECT id, search_text FROM knowledge_chunks "
                        "WHERE embedding IS NULL ORDER BY id")
            pending = cur.fetchall()

        print(f"{total} chunks in table, {len(pending)} without an embedding.")
        if not pending:
            print("Nothing to do.")
            return

        embedder = Embedder(GEMINI_API_KEY)
        done = errors = 0

        for i, (chunk_id, search_text) in enumerate(pending, start=1):
            try:
                # RETRIEVAL_DOCUMENT, matching how repair documents are
                # indexed: the query side uses RETRIEVAL_QUERY, and mixing
                # the two task types degrades similarity.
                vector = embedder.embed(
                    search_text,
                    task_type="RETRIEVAL_DOCUMENT",
                    operation="technical_chunk_embed",
                )
                with conn.cursor() as cur:
                    cur.execute(
                        "UPDATE knowledge_chunks SET embedding = %s WHERE id = %s",
                        (vector, chunk_id),
                    )
                done += 1
            except Exception as e:
                # Same policy as the GUP path: log and carry on. A chunk left
                # without a vector is simply picked up by the next run.
                errors += 1
                print(f"  ERROR embedding chunk {chunk_id}: {e}")

            if i % COMMIT_EVERY == 0:
                conn.commit()
            if i % 100 == 0 or i == len(pending):
                print(f"  embedded {i}/{len(pending)} chunks...")

        conn.commit()
        duration = int(time.time() - started)

        with conn.cursor() as cur:
            cur.execute("""
                INSERT INTO ingestion_log
                    (documents_processed, embeddings_generated, edges_created,
                     errors, duration_seconds, notes)
                VALUES (%s, %s, %s, %s, %s, %s)
                RETURNING id
            """, (0, done, 0, errors, duration, "knowledge_chunks embeddings"))
            run_id = cur.fetchone()[0]
        conn.commit()

        logged = log_usage(conn, embedder.usage_records, run_id)

        with conn.cursor() as cur:
            cur.execute("SELECT count(*) FROM knowledge_chunks WHERE embedding IS NULL")
            remaining = cur.fetchone()[0]

        print(f"Done: {done} embedded, {errors} errors, {remaining} still missing, "
              f"{duration}s, {logged} usage rows logged.")
    finally:
        conn.close()


if __name__ == "__main__":
    main()
