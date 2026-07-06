"""
Builds the knowledge graph and embeddings from the resx files + gup_rows
(loaded by services/ingestion - must run first).

Graph edges and embeddings are checked independently, since embeddings are
expensive (real Gemini API calls/cost) while graph edges are cheap:
  - graph_edges already populated -> skip rebuilding it.
  - document_embeddings already populated -> skip regenerating embeddings.
  - GEMINI_API_KEY not set -> embeddings are skipped (graph still builds).
This means adding a key later and re-running picks up exactly the
embeddings step, without re-doing (or re-billing) anything already done.
"""

import os
import sys
import time
import psycopg2
from pgvector.psycopg2 import register_vector
from psycopg2.extras import execute_values

from resx_parser import parse_resx_folder
from graph_builder import build_all_edges
from embedder import Embedder, build_document_embed_text

DATABASE_URL = os.environ.get("OUR_DB", "postgresql://semarepair:semarepair@localhost:5432/semarepair")
RESX_PATH = os.environ.get("RESX_PATH", os.path.join(os.path.dirname(__file__), "..", "..", "Data", "resx_samples"))
GEMINI_API_KEY = os.environ.get("GEMINI_API_KEY")


def fetch_gup_rows(conn) -> list[dict]:
    with conn.cursor() as cur:
        cur.execute("""
            SELECT id_macchina, marca_macchina, modello_macchina,
                   codice_motore_macchina, id_documento
            FROM gup_rows
        """)
        columns = [desc[0] for desc in cur.description]
        return [dict(zip(columns, row)) for row in cur.fetchall()]


def table_count(conn, table: str) -> int:
    with conn.cursor() as cur:
        cur.execute(f"SELECT COUNT(*) FROM {table}")
        return cur.fetchone()[0]


def build_graph(conn, resx_records: list[dict], gup_rows: list[dict]) -> int:
    edges = build_all_edges(resx_records, gup_rows)
    with conn.cursor() as cur:
        cur.execute("TRUNCATE graph_edges")
        execute_values(
            cur,
            "INSERT INTO graph_edges (from_type, from_id, relation, to_type, to_id, language, description) VALUES %s",
            edges,
        )
    conn.commit()
    return len(edges)


DOCUMENT_COLUMNS = [
    "id_documento", "language", "sigla_documento", "tipo_ris", "titolo",
    "impianto", "dispositivo", "anomalia", "causa", "intervento",
    "procedura", "nota", "reliability",
]


def build_documents(conn, resx_records: list[dict]) -> int:
    values = [tuple(r[c] for c in DOCUMENT_COLUMNS) for r in resx_records]
    with conn.cursor() as cur:
        cur.execute("TRUNCATE documents")
        execute_values(
            cur,
            f"INSERT INTO documents ({', '.join(DOCUMENT_COLUMNS)}) VALUES %s",
            values,
        )
    conn.commit()
    return len(values)


def generate_embeddings(conn, embedder: Embedder, resx_records: list[dict]) -> tuple[int, int]:
    """Generates document_embeddings and symptom_embeddings for every resx
    record. Returns (embeddings_generated, errors). A failure on one record
    is logged and skipped - it never aborts the whole run (section 9.1).
    embedder is constructed by the caller (not here) so its accumulated
    usage_records survive after this function returns - see main()."""
    generated = 0
    errors = 0

    with conn.cursor() as cur:
        for i, record in enumerate(resx_records, start=1):
            id_documento = record["id_documento"]
            language = record["language"]

            try:
                embed_text = build_document_embed_text(record)
                doc_vector = embedder.embed(embed_text, task_type="RETRIEVAL_DOCUMENT", operation="document_embed")
                cur.execute(
                    "INSERT INTO document_embeddings (id_documento, language, embed_text, embedding) "
                    "VALUES (%s, %s, %s, %s)",
                    (id_documento, language, embed_text, doc_vector),
                )
                generated += 1

                if record["anomalia"]:
                    sym_vector = embedder.embed(record["anomalia"], task_type="RETRIEVAL_DOCUMENT", operation="symptom_embed")
                    cur.execute(
                        "INSERT INTO symptom_embeddings (id_documento, language, anomalia, embedding) "
                        "VALUES (%s, %s, %s, %s)",
                        (id_documento, language, record["anomalia"], sym_vector),
                    )
                    generated += 1
            except Exception as e:
                errors += 1
                print(f"  ERROR embedding {id_documento}_{language}: {e}")

            if i % 50 == 0 or i == len(resx_records):
                print(f"  embedded {i}/{len(resx_records)} records...")

    conn.commit()
    return generated, errors


USAGE_COLUMNS = [
    "occurred_at", "service_name", "operation", "model", "prompt_tokens",
    "completion_tokens", "total_tokens", "is_estimated", "cost_usd",
    "session_id", "ingestion_run_id", "raw_usage_json",
]


def log_usage(conn, usage_records: list[dict], ingestion_run_id: int) -> int:
    if not usage_records:
        return 0
    values = [
        tuple(r.get(c, ingestion_run_id if c == "ingestion_run_id" else None) for c in USAGE_COLUMNS)
        for r in usage_records
    ]
    with conn.cursor() as cur:
        execute_values(
            cur,
            f"INSERT INTO gemini_usage_log ({', '.join(USAGE_COLUMNS)}) VALUES %s",
            values,
        )
    conn.commit()
    return len(values)


def main():
    started = time.time()
    conn = psycopg2.connect(DATABASE_URL)
    register_vector(conn)

    try:
        with open(os.path.join(os.path.dirname(__file__), "schema.sql")) as f:
            schema_sql = f.read()
        with conn.cursor() as cur:
            cur.execute(schema_sql)
        conn.commit()

        documents_already_built = table_count(conn, "documents") > 0
        graph_already_built = table_count(conn, "graph_edges") > 0
        embeddings_already_built = table_count(conn, "document_embeddings") > 0

        if (documents_already_built and graph_already_built
                and (embeddings_already_built or not GEMINI_API_KEY)):
            print("documents and graph_edges already built, and embeddings are "
                  "either already built or GEMINI_API_KEY is not set - nothing to do.")
            return

        gup_rows = fetch_gup_rows(conn)
        if not gup_rows:
            print("gup_rows is empty - run services/ingestion first.")
            sys.exit(1)

        resx_records = parse_resx_folder(RESX_PATH)
        documents_processed = len({r["id_documento"] for r in resx_records})

        if documents_already_built:
            print("documents already populated, skipping.")
        else:
            doc_rows = build_documents(conn, resx_records)
            print(f"Inserted {doc_rows} rows into documents.")

        edges_created = 0
        if graph_already_built:
            print("graph_edges already populated, skipping graph rebuild.")
        else:
            edges_created = build_graph(conn, resx_records, gup_rows)
            print(f"Built {edges_created} graph edges.")

        embeddings_generated = 0
        errors = 0
        usage_records: list[dict] = []
        if embeddings_already_built:
            print("document_embeddings already populated, skipping embedding generation.")
            notes = "Already loaded - skipped"
        elif not GEMINI_API_KEY:
            print("GEMINI_API_KEY not set, skipping embedding generation.")
            notes = "GEMINI_API_KEY not set, embeddings skipped"
        else:
            print("Generating embeddings (this calls the Gemini API for every record)...")
            embedder = Embedder(GEMINI_API_KEY)
            embeddings_generated, errors = generate_embeddings(conn, embedder, resx_records)
            usage_records = embedder.usage_records
            notes = "Graph edges + embeddings"

        duration = int(time.time() - started)

        with conn.cursor() as cur:
            cur.execute("""
                INSERT INTO ingestion_log
                    (documents_processed, embeddings_generated, edges_created, errors, duration_seconds, notes)
                VALUES (%s, %s, %s, %s, %s, %s)
                RETURNING id
            """, (documents_processed, embeddings_generated, edges_created, errors, duration, notes))
            ingestion_run_id = cur.fetchone()[0]
        conn.commit()

        usage_rows_logged = log_usage(conn, usage_records, ingestion_run_id)

        print(f"Done: {edges_created} edges created, {embeddings_generated} embeddings generated, "
              f"{documents_processed} documents, {duration}s, {errors} errors, "
              f"{usage_rows_logged} usage rows logged.")
    finally:
        conn.close()


if __name__ == "__main__":
    main()
