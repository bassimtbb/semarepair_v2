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
    created_at  TIMESTAMPTZ DEFAULT NOW()
);

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
