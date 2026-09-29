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

-- Matches idx_doc_emb_hnsw / idx_sym_emb_hnsw in schema.sql - same method,
-- same operator class, because the search uses the same <=> cosine distance.
-- At 1 252 rows the planner picks a sequential scan either way and this
-- changes nothing measurable; it is here so the table does not become the one
-- embedding table without an index the day a real archive is loaded.
CREATE INDEX IF NOT EXISTS idx_chunks_hnsw ON knowledge_chunks
    USING hnsw (embedding vector_cosine_ops);

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
--
-- search_text is in the key too, and for a reason found the hard way: it was
-- left out at first, so improving what gets embedded changed no row's
-- identity and the seeder reported "0 added, 0 pruned" while the stale
-- vectors stayed in place. The identity has to cover the indexed text, or a
-- retrieval fix silently does nothing.
--- The identity above is hashed rather than indexed column by column, and it
--- has to be. A btree index row cannot exceed 2704 bytes, and listing these
--- columns directly put the whole chunk text inside the key: the Ducato's
--- short entries fitted, the Fiat 500's manual sections did not, and the
--- seeder died with "index row size 2840 exceeds btree version 4 maximum".
--- The failure scales with content length, so it was always going to arrive
--- with the first richer vehicle.
---
--- md5 of the concatenation keeps exactly the identity described above while
--- making every key 16 bytes. CHR(31), the ASCII unit separator, delimits the
--- fields so that concatenation cannot blur two different splits into one key
--- (label='ab', value='' must not collide with label='a', value='b'); it
--- cannot appear in the source text, which is XML-derived prose.
---
--- Nothing else depends on the index shape: the seeder's upsert is a bare
--- ON CONFLICT DO NOTHING, which catches any unique violation, and its prune
--- step compares the columns explicitly in SQL rather than through the index.
CREATE UNIQUE INDEX IF NOT EXISTS idx_chunks_identity
    ON knowledge_chunks (id_documento, language, kind,
                         md5(COALESCE(heading, '')   || CHR(31) ||
                             COALESCE(reference, '') || CHR(31) ||
                             COALESCE(label, '')     || CHR(31) ||
                             COALESCE(value, '')     || CHR(31) ||
                             COALESCE(unit, '')      || CHR(31) ||
                             COALESCE(body, '')      || CHR(31) ||
                             search_text));
