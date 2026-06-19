"""
Generates document_embeddings and symptom_embeddings via Gemini's
gemini-embedding-001 model (768-dim, matching the vector(768) columns).
See docs/EmbeddingAndGraph_Technical.md section 2, and section 9.1 for the
retry policy (retry up to 5 times, then skip and log - never crash the run).
"""

import time
from google import genai
from google.genai import types

EMBEDDING_MODEL = "gemini-embedding-001"
EMBEDDING_DIMENSIONS = 768
RETRY_ATTEMPTS = 5


def build_document_embed_text(record: dict) -> str:
    """Full document text. Deliberately excludes procedura/nota - they
    describe HOW to fix, which would dilute similarity with repair
    vocabulary unrelated to the fault itself (section 2 Step 2)."""
    header = f"{record['sigla_documento'] or ''} {record['titolo'] or ''}".strip()
    lines = [header] if header else []
    if record["impianto"]:
        lines.append(f"Impianto: {record['impianto']}")
    if record["dispositivo"]:
        lines.append(f"Dispositivo: {record['dispositivo']}")
    if record["anomalia"]:
        lines.append(f"Anomalia: {record['anomalia']}")
    if record["causa"]:
        lines.append(f"Causa: {record['causa']}")
    if record["intervento"]:
        lines.append(f"Intervento: {record['intervento']}")
    if record["fault_codes"]:
        lines.append(" ".join(record["fault_codes"]))
    return "\n".join(lines)


class Embedder:
    def __init__(self, api_key: str):
        self._client = genai.Client(api_key=api_key)

    def embed(self, text: str, task_type: str = "RETRIEVAL_DOCUMENT") -> list[float]:
        last_error: Exception = RuntimeError("embed() called with RETRY_ATTEMPTS=0")
        for attempt in range(RETRY_ATTEMPTS):
            try:
                response = self._client.models.embed_content(
                    model=EMBEDDING_MODEL,
                    contents=text,
                    config=types.EmbedContentConfig(
                        task_type=task_type,
                        output_dimensionality=EMBEDDING_DIMENSIONS,
                    ),
                )
                return response.embeddings[0].values
            except Exception as e:
                last_error = e
                time.sleep(2 ** attempt)
        raise last_error
