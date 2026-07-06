"""
Generates document_embeddings and symptom_embeddings via Gemini's
gemini-embedding-001 model (768-dim, matching the vector(768) columns).
See docs/EmbeddingAndGraph_Technical.md section 2, and section 9.1 for the
retry policy (retry up to 5 times, then skip and log - never crash the run).
"""

import json
import math
import os
import time
from datetime import datetime, timezone
from google import genai
from google.genai import types

EMBEDDING_MODEL = "gemini-embedding-001"
EMBEDDING_DIMENSIONS = 768
RETRY_ATTEMPTS = 5

PRICING_PATH = os.environ.get(
    "GEMINI_PRICING_PATH",
    os.path.join(os.path.dirname(__file__), "..", "..", "config", "gemini-pricing.json"),
)


def load_pricing() -> dict:
    with open(PRICING_PATH) as f:
        return json.load(f)


# Gemini's embedContent endpoint never returns usageMetadata the way
# generateContent does (see docs/log-dashboard.md section 0) - this is the
# only way to get a token figure for an embedding call at all, and it's
# always an estimate, never a measured fact. is_estimated=True on every
# record this produces makes that visible downstream rather than silently
# presenting a guess as if it were real.
def estimate_usage(pricing: dict, text: str, model: str, service_name: str, operation: str) -> dict:
    chars_per_token = pricing["estimation"]["characters_per_token"]
    estimated_tokens = math.ceil(len(text) / chars_per_token)

    model_pricing = pricing["models"].get(model)
    cost_usd = None
    if model_pricing is not None:
        price_per_million = model_pricing["input_price_per_million_tokens_usd"]
        cost_usd = estimated_tokens / 1_000_000 * price_per_million

    return {
        "occurred_at": datetime.now(timezone.utc),
        "service_name": service_name,
        "operation": operation,
        "model": model,
        "prompt_tokens": estimated_tokens,
        "completion_tokens": None,
        "total_tokens": estimated_tokens,
        "is_estimated": True,
        "cost_usd": cost_usd,
        "session_id": None,
        "raw_usage_json": json.dumps({
            "estimated_from_text_length": len(text),
            "characters_per_token": chars_per_token,
        }),
    }


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
        self._pricing = load_pricing()
        # Accumulated across the whole run, same shape as how seeder.py
        # already accumulates graph edges before one bulk insert - read by
        # seeder.py after generate_embeddings() completes, not written to
        # the database from inside this class (this script owns one
        # connection, opened by seeder.py).
        self.usage_records: list[dict] = []

    def embed(self, text: str, task_type: str = "RETRIEVAL_DOCUMENT", operation: str = "document_embed") -> list[float]:
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
                self.usage_records.append(
                    estimate_usage(self._pricing, text, EMBEDDING_MODEL, "ingestion-resx", operation)
                )
                return response.embeddings[0].values
            except Exception as e:
                last_error = e
                time.sleep(2 ** attempt)
        raise last_error
