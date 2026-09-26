-- Extension v2 - see docs/Architecture_Extension_v2.md section 3.
--
-- One table, not three. A "chunk" is one searchable unit, whatever its
-- origin: a technical value, a schematic legend entry, or a prose chapter.
-- The `kind` column tells them apart, and the frontend renders by kind.
--
-- The obvious design would give facts, sections and legends a table each,
-- plus an embedding table each: six tables, three parsers, three search
-- paths. This is deliberately the other way round - one embedding pass, one
-- query, one migration, and DROP TABLE knowledge_chunks undoes the whole
-- feature. That reversibility is worth more than the normalisation we give
-- up, and the table will likely need splitting once the product grows.
--
-- Additive by design: no existing table is touched. documents, gup_rows,
-- graph_edges, document_embeddings and symptom_embeddings are untouched, so
-- the current demo keeps working whether or not this table is populated.

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS knowledge_chunks (
    id            SERIAL PRIMARY KEY,
    id_documento  TEXT NOT NULL,
    language      TEXT NOT NULL,

    -- 'fact'    a value: fuse rating, torque, bulb type, engine spec
    -- 'legend'  a component on a wiring diagram or location list
    -- 'section' a prose chapter from a non-GUP document
    kind          TEXT NOT NULL,

    heading       TEXT,   -- Gruppo, fuse-box title, or chapter title
    label         TEXT,   -- Dato, Descrizione, NomeComp
    value         TEXT,   -- Valore, fuse rating, Localizzazione
    unit          TEXT,   -- UnitaMis
    reference     TEXT,   -- Numero on the diagram: F04, H1, S1
    body          TEXT,   -- prose (sections only), NULL for facts

    -- What was actually embedded, stored rather than recomputed - same
    -- reasoning as document_embeddings.embed_text: a result can always be
    -- traced back to the text that produced it.
    search_text   TEXT NOT NULL,

    -- PDF/image id from RifIDFilePDF, resolved against Data/PDF at serve
    -- time. NULL when the chunk has no attachment.
    asset_id      TEXT,

    embedding     vector(768),
    created_at    TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_chunks_doc  ON knowledge_chunks (id_documento, language);
CREATE INDEX IF NOT EXISTS idx_chunks_kind ON knowledge_chunks (kind, language);

-- Lets a re-run replace a document's chunks without duplicating them, and
-- makes the seeder's ON CONFLICT clause possible. reference/label can repeat
-- within a document (two fuse boxes both list an "F01"), so heading is part
-- of the key.
--
-- `value` and `unit` are in the key, and that is load-bearing rather than
-- defensive. Without them this index silently dropped real rows: document
-- 25059516 lists "Indicatore di direzione laterale - Lampadina" twice with
-- bulb types WY5W and W5W, and 50011102 gives refrigerant quantities of
-- 650 g and 1500 g under one label for different body variants. Same label,
-- different answer - collapsing them would hand a mechanic the wrong part.
CREATE UNIQUE INDEX IF NOT EXISTS idx_chunks_identity
    ON knowledge_chunks (id_documento, language, kind,
                         COALESCE(heading, ''), COALESCE(reference, ''),
                         COALESCE(label, ''), COALESCE(value, ''),
                         COALESCE(unit, ''), COALESCE(body, ''));
