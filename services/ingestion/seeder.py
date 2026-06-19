"""
Loads GUP_PER_IA.xlsx into the gup_rows table (Our PostgreSQL).
Skips loading if gup_rows is already populated - safe to run on every
`docker compose up` without reloading data that's already there.
"""

import os
import sys
import psycopg2
from psycopg2.extras import execute_values

from parser import parse_excel

DATABASE_URL = os.environ.get("OUR_DB", "postgresql://semarepair:semarepair@localhost:5432/semarepair")
EXCEL_PATH = os.environ.get("EXCEL_PATH", os.path.join(os.path.dirname(__file__), "..", "..", "Data", "GUP_PER_IA.xlsx"))

COLUMNS = [
    "id_macchina", "marca_macchina", "modello_macchina",
    "anno_inizio_macchina", "anno_fine_macchina", "alimentazione_macchina",
    "motorizzazione_macchina", "kw_macchina", "cavalli_macchina",
    "codice_motore_macchina", "id_documento",
]


def main():
    conn = psycopg2.connect(DATABASE_URL)
    try:
        with open(os.path.join(os.path.dirname(__file__), "schema.sql")) as f:
            schema_sql = f.read()
        with conn.cursor() as cur:
            cur.execute(schema_sql)
        conn.commit()

        with conn.cursor() as cur:
            cur.execute("SELECT COUNT(*) FROM gup_rows")
            existing = cur.fetchone()[0]
        if existing > 0:
            print(f"gup_rows already has {existing} rows, skipping load.")
            return

        rows = parse_excel(EXCEL_PATH)
        if not rows:
            print("No rows parsed, aborting.")
            sys.exit(1)

        with conn.cursor() as cur:
            values = [tuple(r[c] for c in COLUMNS) for r in rows]
            execute_values(
                cur,
                f"INSERT INTO gup_rows ({', '.join(COLUMNS)}) VALUES %s",
                values,
            )
        conn.commit()
        print(f"Inserted {len(values)} rows into gup_rows.")
    finally:
        conn.close()


if __name__ == "__main__":
    main()
