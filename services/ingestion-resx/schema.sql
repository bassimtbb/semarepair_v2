-- Knowledge graph + embedding tables. See docs/SemaRepair_Architecture.md
-- section 6.8. document_embeddings/symptom_embeddings are created here but
-- not yet populated by seeder.py - embedding generation needs a Gemini API
-- key (see progress.md), and is a later pass.

CREATE EXTENSION IF NOT EXISTS vector;

-- Document content, parsed from the resx files. Our local stand-in for
-- "their content store" until production SQL Server access exists - see
-- docs/SemaRepair_Architecture.md section 8.2 ("never store copies of
-- their repair content" assumes Their SQL Server is queryable at request
-- time, which we don't have yet).
CREATE TABLE IF NOT EXISTS documents (
    id_documento        TEXT NOT NULL,
    language            TEXT NOT NULL,
    sigla_documento     TEXT,
    tipo_ris            TEXT,
    titolo              TEXT,
    impianto            TEXT,
    dispositivo         TEXT,
    anomalia            TEXT,
    causa               TEXT,
    intervento          TEXT,
    procedura           TEXT,
    nota                TEXT,
    reliability         INTEGER,
    created_at          TIMESTAMPTZ DEFAULT NOW(),
    PRIMARY KEY (id_documento, language)
);

CREATE TABLE IF NOT EXISTS graph_edges (
    id          SERIAL PRIMARY KEY,
    from_type   TEXT NOT NULL,
    from_id     TEXT NOT NULL,
    relation    TEXT NOT NULL,
    to_type     TEXT NOT NULL,
    to_id       TEXT NOT NULL,
    language    TEXT,
    weight      FLOAT DEFAULT 1.0,
    description TEXT,
    created_at  TIMESTAMPTZ DEFAULT NOW()
);

-- Only populated for CONTAINS_FAULT edges (the DTC code's own explanation,
-- e.g. "Relazione interruttore freno 1-2 - Rapporto errato" for P0504),
-- taken verbatim from the specific document's own resx text - see
-- resx_parser.py's fault_code_descriptions. Added after graph_edges
-- already existed in deployed databases, so CREATE TABLE IF NOT EXISTS
-- above wouldn't add it on its own - this ALTER is what actually applies
-- to an already-running database; idempotent, safe on every startup.
ALTER TABLE graph_edges ADD COLUMN IF NOT EXISTS description TEXT;

CREATE INDEX IF NOT EXISTS idx_graph_from     ON graph_edges (from_type, from_id);
CREATE INDEX IF NOT EXISTS idx_graph_to       ON graph_edges (to_type, to_id);
CREATE INDEX IF NOT EXISTS idx_graph_relation ON graph_edges (relation);
CREATE INDEX IF NOT EXISTS idx_graph_lang     ON graph_edges (language);

CREATE TABLE IF NOT EXISTS document_embeddings (
    id           SERIAL PRIMARY KEY,
    id_documento TEXT NOT NULL,
    language     TEXT NOT NULL,
    embed_text   TEXT,
    embedding    vector(768),
    created_at   TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_doc_emb_hnsw ON document_embeddings
    USING hnsw (embedding vector_cosine_ops);

CREATE TABLE IF NOT EXISTS symptom_embeddings (
    id           SERIAL PRIMARY KEY,
    id_documento TEXT NOT NULL,
    language     TEXT NOT NULL,
    anomalia     TEXT NOT NULL,
    embedding    vector(768),
    created_at   TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_sym_emb_hnsw ON symptom_embeddings
    USING hnsw (embedding vector_cosine_ops);
CREATE INDEX IF NOT EXISTS idx_sym_emb_lang ON symptom_embeddings (language);

CREATE TABLE IF NOT EXISTS ingestion_log (
    id                   SERIAL PRIMARY KEY,
    run_at               TIMESTAMPTZ DEFAULT NOW(),
    documents_processed  INTEGER,
    embeddings_generated INTEGER,
    edges_created        INTEGER,
    errors               INTEGER,
    duration_seconds     INTEGER,
    notes                TEXT
);

-- One row per Gemini API call, written by whichever service made the call
-- (chat-service, search-service, ingestion-resx) - see docs/log-dashboard.md.
-- is_estimated is true for every embedContent-derived row (P0148-style
-- fidelity rule: Gemini's embedContent response never returns token usage,
-- unlike generateContent, so those rows can only ever carry an estimate,
-- and that must stay visible rather than presented as a measured fact).
-- cost_usd is computed once at write time from config/gemini-pricing.json
-- and never recomputed in place - a later price change affects new rows
-- only, like a real invoice. raw_usage_json keeps the verbatim source
-- fact (the real usageMetadata object, or the estimation input) so cost
-- can be recalculated later if pricing/estimation assumptions change.
CREATE TABLE IF NOT EXISTS gemini_usage_log (
    id                 BIGSERIAL PRIMARY KEY,
    occurred_at        TIMESTAMPTZ DEFAULT NOW(),
    service_name       TEXT NOT NULL,
    operation          TEXT,
    model              TEXT NOT NULL,
    prompt_tokens      INTEGER,
    completion_tokens  INTEGER,
    total_tokens       INTEGER,
    is_estimated       BOOLEAN NOT NULL DEFAULT FALSE,
    cost_usd           NUMERIC(12, 8),
    session_id         TEXT,
    ingestion_run_id   INTEGER REFERENCES ingestion_log(id),
    raw_usage_json     JSONB
);

CREATE INDEX IF NOT EXISTS idx_usage_occurred_at ON gemini_usage_log (occurred_at);
CREATE INDEX IF NOT EXISTS idx_usage_service     ON gemini_usage_log (service_name);
CREATE INDEX IF NOT EXISTS idx_usage_model       ON gemini_usage_log (model);
