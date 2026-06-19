-- Vehicle <-> document mapping table, loaded from GUP_PER_IA.xlsx.
-- Document content (title, chapters, fault codes) is parsed separately
-- from the resx files in a later ingestion phase.

CREATE TABLE IF NOT EXISTS gup_rows (
    id                       SERIAL PRIMARY KEY,
    id_macchina              TEXT NOT NULL,
    marca_macchina           TEXT,
    modello_macchina         TEXT,
    anno_inizio_macchina     INTEGER,
    anno_fine_macchina       INTEGER,
    alimentazione_macchina   TEXT,
    motorizzazione_macchina  TEXT,
    kw_macchina              INTEGER,
    cavalli_macchina         INTEGER,
    codice_motore_macchina   TEXT,
    id_documento             TEXT NOT NULL,
    created_at               TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_gup_rows_id_documento ON gup_rows (id_documento);
CREATE INDEX IF NOT EXISTS idx_gup_rows_id_macchina  ON gup_rows (id_macchina);
