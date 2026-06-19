# Embedding, Graph, and Search — Technical Explanation

> How the data flows from raw document to search result.
> Uses real data from the 4 sample documents: GUP97380, GUP97381,
> GUP97383, GUP97385.

---

## Table of Contents

1. [What is an Embedding](#1-what-is-an-embedding)
2. [How Embeddings are Generated — Ingestion Pipeline](#2-how-embeddings-are-generated--ingestion-pipeline)
3. [How Vector Search Works](#3-how-vector-search-works)
4. [Why Vector Search Alone Fails](#4-why-vector-search-alone-fails)
5. [How the Graph is Built](#5-how-the-graph-is-built)
6. [How Graph Search Works](#6-how-graph-search-works)
7. [The Combined Flow — Graph First, Vector Fallback](#7-the-combined-flow--graph-first-vector-fallback)
8. [Full End-to-End Flow — Real Example](#8-full-end-to-end-flow--real-example)
9. [The Validation Layer](#9-the-validation-layer)
10. [Hallucination Protection](#10-hallucination-protection)
11. [Storage Numbers — Real Scale](#11-storage-numbers--real-scale)

---

## 1. What is an Embedding

An embedding converts text into numbers so that a computer can measure
how similar two pieces of text are mathematically.

**The core idea:**
Two sentences that mean similar things produce number arrays that are
mathematically close. Two sentences about different topics produce arrays
that are far apart.

```
Text input:
  "spia avaria motore con freni difettosi"

Embedding output (768 numbers):
  [0.023, -0.156, 0.891, 0.034, -0.445, 0.201, ..., 0.112]
   dim1    dim2    dim3   dim4    dim5              dim768
```

Think of each number as a coordinate in a 768-dimensional space.
Documents about similar topics cluster together. Different topics are far apart.

```
High-dimensional space (simplified to 2D):

  "freni" cluster:
    GUP97380 — Interruttore stop, spia avaria motore

  "iniezione" cluster:
    GUP97381 — Pompa alta pressione
    GUP97383 — Filtro gasolio
    GUP97385 — Candelette

  "climatizzatore" cluster:
    GUP97468 — Ventola del radiatore funzionamento continuo
```

**Why 768 dimensions?**
`gemini-embedding-001` always produces exactly 768 numbers regardless
of input length. More dimensions = more information = better discrimination.
768 is the native output size of this model.

---

## 2. How Embeddings are Generated — Ingestion Pipeline

The ingestion service generates **two separate embeddings** per document
per language. This is a key design decision — explained in Section 4.

### Step 1 — Read the raw document

```xml
<!-- 199309631_IT.resx -->
<XDOCUMENTO>
  <ID>199309631</ID>
  <DOCNumber>GUP97380</DOCNumber>
  <Titolo>Accensione spia avaria motore, con veicolo correttamente funzionante</Titolo>
  <InfoDoc>FRENI | P0504 | C1215 | B1024 | INTERRUTTORE | STOP | ...</InfoDoc>
  <XCAPITOLO>
    <Capitolo>Identificazione del sistema / guasto</Capitolo>
    <Corpo>
      Impianto: Freni
      Dispositivo: Interruttore stop
      Anomalia: Accensione spia avaria motore, con veicolo correttamente funzionante
      Causa: Interruttore stop difettoso
    </Corpo>
  </XCAPITOLO>
  <XCAPITOLO>
    <Capitolo>Procedura di riparazione</Capitolo>
    <Corpo>
      Intervento: Verificare il corretto funzionamento dell'interruttore stop...
    </Corpo>
  </XCAPITOLO>
</XDOCUMENTO>
```

### Step 2 — Build embed_text for document_embeddings

Combines all semantically meaningful fields into one string:

```python
embed_text = f"""
{sigla} {titolo}
Impianto: {impianto}
Dispositivo: {dispositivo}
Anomalia: {anomalia}
Causa: {causa}
Intervento: {intervento}
{fault_codes}
"""
```

**Real output for GUP97380:**
```
GUP97380 Accensione spia avaria motore con veicolo correttamente funzionante
Impianto: Freni
Dispositivo: Interruttore stop
Anomalia: Accensione spia avaria motore con veicolo correttamente funzionante
Causa: Interruttore stop difettoso
Intervento: Verificare il corretto funzionamento dell interruttore stop
P0504 C1215 B1024
```

> The full procedura text is excluded because it describes HOW to fix —
> which would pollute similarity with repair vocabulary unrelated to the
> symptom description.

### Step 3 — Build embed_text for symptom_embeddings

Uses ONLY the anomalia field — the precise technical description of the fault:

```python
symptom_embed_text = anomalia
# "Accensione spia avaria motore, con veicolo correttamente funzionante"
```

**Why this is separate — see Section 4 for full explanation.**

### Step 4 — Send both to Gemini Embedding API

```python
# Full document embedding
doc_response = gemini.embed_content(
    model="gemini-embedding-001",
    content=embed_text,
    task_type="RETRIEVAL_DOCUMENT"
)
doc_vector = doc_response.embedding  # 768 floats

# Symptom embedding (anomalia only)
sym_response = gemini.embed_content(
    model="gemini-embedding-001",
    content=symptom_embed_text,
    task_type="RETRIEVAL_DOCUMENT"
)
sym_vector = sym_response.embedding  # 768 floats
```

Each API call: ~50–100ms.
For 20,000 documents × 4 languages × 2 embeddings = 160,000 API calls.
At 100ms each = ~4.4 hours. This is why ingestion runs at night.

### Step 5 — Store both in PostgreSQL

```sql
-- Full document embedding
INSERT INTO document_embeddings (id_documento, language, embed_text, embedding)
VALUES ('199309631', 'it', 'GUP97380 Accensione spia...', '[0.023,-0.156,0.891,...]');

-- Anomalia-only embedding
INSERT INTO symptom_embeddings (id_documento, language, anomalia, embedding)
VALUES ('199309631', 'it',
        'Accensione spia avaria motore, con veicolo correttamente funzionante',
        '[0.031,-0.142,0.903,...]');
```

Size per embedding: 768 × 4 bytes = 3,072 bytes ≈ 3 KB.

### Step 6 — Real anomalia values stored in symptom_embeddings

```
id_documento  language  anomalia
------------  --------  --------
199309631     it        "Accensione spia avaria motore, con veicolo correttamente funzionante"
199309633     it        "Sporadicamente il veicolo presenta scarse prestazioni con notevoli cali di potenza"
199309673     it        "Il veicolo presenta un notevole calo di prestazioni, la spia avaria motore (rossa)"
199309676     it        "Il veicolo presenta un notevole calo di prestazioni, la spia avaria motore (rossa)"
```

These short precise descriptions are exactly what mechanics describe.
Vector similarity between a mechanic's symptom and these anomalia values
is much more accurate than comparing to full documents.

---

## 3. How Vector Search Works

### Step 1 — Embed the query

```python
query = "spia avaria motore freni"

query_vector = gemini.embed_content(
    model="gemini-embedding-001",
    content=query,
    task_type="RETRIEVAL_QUERY"   # different task_type for queries
)
# Result: [0.019, -0.143, 0.876, ...]
```

### Step 2 — Cosine distance in PostgreSQL

```sql
SELECT id_documento, embedding <=> $queryVector AS distance
FROM document_embeddings
WHERE language = 'it'
ORDER BY embedding <=> $queryVector
LIMIT 5;
```

The `<=>` operator is pgvector's cosine distance.
- 0.0 = identical meaning
- 0.1 = very similar
- 0.5 = somewhat related
- 1.0 = completely different

### Step 3 — HNSW index makes it fast

```sql
CREATE INDEX idx_doc_emb_hnsw ON document_embeddings
    USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);
```

Without index: scan all 80,000 rows → ~500ms
With HNSW: approximate nearest neighbor → ~5ms
99%+ recall — occasionally misses a very close match, acceptable trade-off.

---

## 4. Why Vector Search Alone Fails — And Why Two Embedding Tables

### The fundamental limit

Vector similarity measures **word meaning**, not **technical correctness**.
Documents can score high similarity because they share common vocabulary
even if they describe completely different faults.

**Example — query: "ventola del radiatore funzionamento continuo"**

```
Full document_embeddings search results:

  0.08  GUP97468 — Funzionamento continuo ventola del radiatore    ✅ CORRECT
  0.29  GUP97512 — Ventilatore abitacolo mancato funzionamento     ❌ WRONG
  0.31  GUP97431 — Mancato funzionamento ventilatore               ❌ WRONG
```

GUP97512 scores 0.29 because both documents contain:
"ventola", "funzionamento", "veicolo", "componente", "verificare".
The embedding does not know "ventola del radiatore" (cooling fan)
and "ventilatore abitacolo" (cabin blower) are completely different systems.

### Why symptom_embeddings solves this

When searching `symptom_embeddings`, we compare the query only against
short precise `anomalia` values:

```
Query: "ventola del radiatore funzionamento continuo"

symptom_embeddings search:
  0.07  "Funzionamento continuo alla massima velocità della ventola del radiatore"  ✅
  0.44  "Ventilatore abitacolo non funzionante"                                     ✗
  0.61  "Mancato funzionamento ventola raffreddamento"                              ✗
```

The gap between the correct match (0.07) and the next candidate (0.44)
is much larger than in the full document search. This makes the result
far more reliable.

### When each table is used

| Situation | Table used | Reason |
|-----------|-----------|--------|
| No car confirmed, mechanic types symptom | `symptom_embeddings` | Short precise anomalia values = better match |
| Car confirmed, mechanic types symptom | `document_embeddings` | Rerank within car's docs — broader context helps |

The key insight: when no car is confirmed, we are trying to find WHICH
document most closely matches the symptom. The anomalia field is the
ground truth for this comparison. When the car IS confirmed, we already
have the right document set — we just need to rank them, and full document
embeddings provide richer ranking signals.

---

## 5. How the Graph is Built

For every document, the ingestion service creates rows in `graph_edges`.

### Building edges for GUP97380 (real data)

**Source from resx:**
```
id_documento:  199309631
sigla:         GUP97380
infoDoc:       FRENI | P0504 | C1215 | B1024 | INTERRUTTORE | STOP | ...
impianto:      Freni
dispositivo:   Interruttore stop
anomalia:      Accensione spia avaria motore con veicolo funzionante
causa:         Interruttore stop difettoso
```

**Car-document mapping from SQL Server:**
```
vehicle_id: FO2983 → document_id: 199309631
```

**Edge a — Car → DOCUMENTED_IN → Document:**
```sql
INSERT INTO graph_edges (from_type, from_id, relation, to_type, to_id)
VALUES ('car', 'FO2983', 'DOCUMENTED_IN', 'document', '199309631');
```

**Edge b — Document → CONTAINS_FAULT → FaultCode:**
```python
fault_codes = re.findall(r'\b[PCBU]\d{4}\b', infoDoc)
# ['P0504', 'C1215', 'B1024']

for code in fault_codes:
    INSERT INTO graph_edges
    VALUES ('document', '199309631', 'CONTAINS_FAULT', 'faultcode', code, 'it')
```

**Edge c — FaultCode → RELATED_TO → FaultCode:**
```python
for code_a, code_b in combinations(fault_codes, 2):
    INSERT INTO graph_edges
    VALUES ('faultcode', code_a, 'RELATED_TO', 'faultcode', code_b, 'it')
# P0504↔C1215, P0504↔B1024, C1215↔B1024
```

**Edge d — Document → AFFECTS_SYSTEM → System:**
```sql
INSERT INTO graph_edges
VALUES ('document', '199309631', 'AFFECTS_SYSTEM', 'system', 'Freni', 'it');
```

**Edge e — Document → INVOLVES_DEVICE → Device:**
```sql
INSERT INTO graph_edges
VALUES ('document', '199309631', 'INVOLVES_DEVICE', 'device', 'Interruttore stop', 'it');
```

**Edge f — Document → HAS_TRANSLATION → Document:**
```python
for lang in ['fr', 'en', 'pt']:
    INSERT INTO graph_edges
    VALUES ('document', '199309631_it', 'HAS_TRANSLATION',
            'document', f'199309631_{lang}', null)
```

**Edge g — Car → SHARES_ENGINE_WITH → Car:**
```python
# Find all cars with same engine code, different brand
others = SELECT id_macchina FROM vehicles
         WHERE engine_code = engine AND id_macchina != car_id

for other in others:
    INSERT INTO graph_edges
    VALUES ('car', car_id, 'SHARES_ENGINE_WITH', 'car', other.id, null)
```

**Final result — all edges for 199309631:**

```
from_type   from_id      relation          to_type    to_id               lang
----------  -----------  ----------------  ---------  ------------------  ----
car         FO2983       DOCUMENTED_IN     document   199309631           null
document    199309631    CONTAINS_FAULT    faultcode  P0504               it
document    199309631    CONTAINS_FAULT    faultcode  C1215               it
document    199309631    CONTAINS_FAULT    faultcode  B1024               it
document    199309631    AFFECTS_SYSTEM    system     Freni               it
document    199309631    INVOLVES_DEVICE   device     Interruttore stop   it
document    199309631    HAS_TRANSLATION   document   199309631_fr        null
document    199309631    HAS_TRANSLATION   document   199309631_en        null
document    199309631    HAS_TRANSLATION   document   199309631_pt        null
faultcode   P0504        RELATED_TO        faultcode  C1215               it
faultcode   P0504        RELATED_TO        faultcode  B1024               it
faultcode   C1215        RELATED_TO        faultcode  B1024               it
```

12 rows describing everything the system knows about this document.

---

## 6. How Graph Search Works

Graph search is SQL queries following edges. No special graph database.
Just PostgreSQL with the `graph_edges` table.

### Search Type 1 — Fault Code Search (exact)

**Mechanic:** `"P0504"` — car FORD Fiesta XUJN confirmed

```sql
-- Step 1: documents containing P0504
SELECT to_id AS id_documento
FROM graph_edges
WHERE from_type = 'faultcode' AND from_id = 'P0504'
  AND relation = 'CONTAINS_FAULT' AND language = 'it';
-- Result: ['199309631', '199309756', '199309812']

-- Step 2: intersect with documents for confirmed car XUJN
SELECT to_id FROM graph_edges
WHERE from_type = 'car' AND from_id = 'XUJN'
  AND relation = 'DOCUMENTED_IN'
  AND to_id IN ('199309631', '199309756', '199309812');
-- Result: ['199309631'] ← only GUP97380 applies to BOTH
```

**This is exact. No probability. No similarity.**
GUP97380 is returned because it is directly connected to BOTH P0504 AND XUJN.

---

### Search Type 2 — System / Device Keyword (pure graph)

**Mechanic:** `"valvola EGR"` — F1AE0481C confirmed

```sql
-- Documents involving EGR valve that also apply to F1AE0481C
SELECT to_id FROM graph_edges
WHERE from_id = 'F1AE0481C' AND relation = 'DOCUMENTED_IN'
INTERSECT
SELECT from_id FROM graph_edges
WHERE to_id = 'Valvola EGR' AND relation = 'INVOLVES_DEVICE';
```

**No embedding generated. Pure graph. Fastest and most precise.**

---

### Search Type 3 — Symptom with confirmed car (Graph + Vector hybrid)

**Mechanic:** `"spia motore accesa scarse prestazioni"` — F1AE0481C confirmed

```sql
-- Step 1: Graph — all documents for this car
SELECT to_id AS id_documento
FROM graph_edges
WHERE from_type = 'car' AND from_id = 'F1AE0481C'
  AND relation = 'DOCUMENTED_IN';
-- Result: ['199309631', '199309633', '199309673', '199309676', ...]

-- Step 2: Vector rerank WITHIN pre-filtered set only
SELECT id_documento, embedding <=> $queryVec AS dist
FROM document_embeddings
WHERE language = 'it'
  AND id_documento IN ('199309631', '199309633', '199309673', '199309676', ...)
ORDER BY dist LIMIT 1;
-- Result: 199309673 (GUP97383) dist=0.08
```

**Key difference from pure vector search:**
The vector comparison only happens within the pre-filtered set for F1AE0481C.
Documents for FORD Fiesta or CITROEN are never considered.
This eliminates wrong-brand results entirely.

---

### Search Type 4 — Symptom with NO car confirmed (symptom_embeddings + Graph)

This type has two steps that work together: Gemini query cleaning,
then symptom_embeddings search, then graph traversal.

#### Step A — Gemini cleans the query inside the tool call

```
Mechanic types:
  "ho un problema con climatizzatore ventola del radiatore funzionamento continuo"

Gemini (Chat Service) applies Rule 11 from system prompt:
  Removes: "ho un problema con" (pure filler — zero technical content)
  Keeps:   "climatizzatore ventola del radiatore funzionamento continuo"
           (the system name AND the full behavior description — nothing
            technical is ever dropped, and nothing is ever invented)

Gemini calls:
  SearchBySymptom(symptom="climatizzatore ventola del radiatore funzionamento continuo")
```

Gemini handles this naturally — no code filter needed. It understands
that "ho un problema con" is a filler phrase regardless of language or dialect.
"Climatizzatore" is a System name, not filler — it is always kept, because
the rule only strips conversational phrases, never technical terms.

#### Step B — Validation (see Section 9)

```
ValidateSymptom("climatizzatore ventola del radiatore funzionamento continuo")
→ 6 words, technical content → ValidationResult.Valid ✅
→ Continue to search
```

#### Step C — Check the graph for a System/Device match BEFORE vector search

This step is easy to skip by mistake — going straight to vector search would
ignore an exact match that might already exist in the graph. Before any
embedding is generated, the Search Service checks whether part of the cleaned
query names a known System or Device:

```sql
SELECT to_id, to_type
FROM graph_edges
WHERE to_type IN ('system', 'device')
  AND language = 'it'
  AND (to_id ILIKE '%climatizzatore%' OR to_id ILIKE '%ventola%');

-- Result: 'Climatizzatore' matches a System node ✅
```

Since "Climatizzatore" exists as a System node, the system could traverse
directly:

```sql
SELECT to_id AS id_documento
FROM graph_edges
WHERE from_type = 'document' AND relation = 'AFFECTS_SYSTEM'
  AND to_id = 'Climatizzatore' AND language = 'it';
```

This would return every document affecting the Climatizzatore system —
exact, no similarity scoring. In this example the system still proceeds to
vector search next (Step D) because the query also contains a precise
behavior description ("funzionamento continuo") that narrows down which
Climatizzatore document is the right one — the graph match alone would
return all Climatizzatore documents, not just the one about the fan running
continuously. Graph and vector are combined: graph narrows the system,
vector narrows the specific symptom within it.

#### Step D — Vector search on symptom_embeddings (anomalia only)

```sql
SELECT id_documento, embedding <=> $queryVec AS dist
FROM symptom_embeddings
WHERE language = 'it'
ORDER BY dist LIMIT 1;
```

**What symptom_embeddings contains (real anomalia values):**
```
"Accensione spia avaria motore, con veicolo correttamente funzionante"          → GUP97380
"Sporadicamente il veicolo presenta scarse prestazioni"                         → GUP97381
"Il veicolo presenta un notevole calo di prestazioni, spia avaria motore rossa" → GUP97383
"Il veicolo presenta un notevole calo di prestazioni, spia avaria motore rossa" → GUP97385
"Funzionamento continuo alla massima velocità della ventola del radiatore"      → GUP97468
```

**Query:** `"climatizzatore ventola del radiatore funzionamento continuo"`

```
distances:
  GUP97468 "Funzionamento continuo alla massima velocità della ventola del radiatore"
           → distance = 0.06  ✅ very close (even closer than before —
              "climatizzatore" reinforces the match since GUP97468's
              Impianto field is exactly "Climatizzatore")

  GUP97380 "Accensione spia avaria motore..."
           → distance = 0.71  ✗ unrelated

  GUP97383 "Notevole calo di prestazioni, spia avaria motore"
           → distance = 0.65  ✗ unrelated
```

Gap between correct (0.06) and next (0.65) is huge → reliable result.
Keeping the system name in the query never hurts the match — it only
adds confirming signal when the anomalia text and the system genuinely
correspond, which is the normal case.

#### Step D — Graph: extract cars from GUP97468

```sql
SELECT from_id AS id_macchina
FROM graph_edges
WHERE to_type = 'document' AND to_id = '199309714'  -- GUP97468
  AND relation = 'DOCUMENTED_IN';

-- Result: FI0370, FI0371, FI0372, FI0373, FI0374, FI0375
--   FIAT Ducato 8140.43S  2000-2002  94kw
--   FIAT Ducato RHV       2002-2006  62kw
--   FIAT Ducato F1AE0481C 2002-2006  81kw
--   ...
```

#### Step E — Return car selection list

```
Bot: "Ho trovato casi documentati per questo problema.
      Seleziona il tuo veicolo:"

  [1] FIAT Ducato 2.8 JTD 8v  — 8140.43S — 2000-2002  [Seleziona]
  [2] FIAT Ducato 2.0 JTD 8v  — RHV      — 2002-2006  [Seleziona]
  [3] FIAT Ducato 2.3 JTD 16v — F1AE0481C — 2002-2006 [Seleziona]
```

Repair document NOT shown yet. Mechanic must confirm car first.

#### Step F — Mechanic confirms car → re-search → show document

```
Mechanic clicks: 8140.43S → confirmed_car = 8140.43S

Auto re-search: SearchBySymptom(
    symptom  = "climatizzatore ventola del radiatore funzionamento continuo",
    engineCode = "8140.43S"
)

→ Search Type 3 (symptom + confirmed car)
→ Graph: all docs for 8140.43S
→ Vector rerank → GUP97468
→ Show repair document ✅
```

---

### Search Type 5 — Cross-brand same engine (SHARES_ENGINE_WITH fallback)

**Mechanic:** `"mancato avviamento"` — CITROEN Jumper 8140.43S confirmed
**System category:** Motore → fallback ALLOWED

```sql
-- Primary search: 0 documents for CITROEN variant
-- Fallback: find cars sharing engine 8140.43S
SELECT to_id AS related_car_id
FROM graph_edges
WHERE from_type = 'car' AND from_id = 'CI_8140.43S'
  AND relation = 'SHARES_ENGINE_WITH';
-- Result: FIAT_8140.43S, PEUGEOT_8140.43S

-- Find documents for related cars matching the symptom
SELECT to_id FROM graph_edges
WHERE from_id IN ('FIAT_8140.43S', 'PEUGEOT_8140.43S')
  AND relation = 'DOCUMENTED_IN';
```

**Bot response (always explicit):**
```
"Non ho trovato casi per il tuo CITROEN Jumper.
 Ho trovato per veicoli con lo stesso motore (8140.43S):
 FIAT Ducato, PEUGEOT Boxer.
 Le procedure potrebbero essere applicabili. Vuoi vedere?"
```

This fallback is ONLY for Motore and Elettrico motore categories.
Freni, Sterzo, Climatizzatore = chassis-specific = no fallback.

---

## 7. The Combined Flow — Graph First, Vector Fallback

```
Query arrives
     │
     ├── Fault code? → Graph exact match (no vector needed)
     │
     ├── Device/System keyword? → Graph exact match (no vector needed)
     │
     ├── Symptom + car confirmed?
     │       → Graph: get car's documents
     │         Vector: rank by similarity within that set (document_embeddings)
     │
     ├── Symptom + NO car?
     │       → Gemini cleans query (keeps all technical terms, even
     │         system/device names mentioned alongside the symptom)
     │         Validate symptom
     │         Check graph for System/Device match in the cleaned query
     │         Vector on symptom_embeddings → find best anomalia match
     │         (graph narrows the system if matched, vector narrows
     │          the specific symptom within it)
     │         Graph: extract cars from matched document
     │         → Return car selection list
     │
     ├── 0 results + Motore category?
     │       → SHARES_ENGINE_WITH fallback
     │         Explicit transparency message
     │
     └── 0 results + chassis-specific system?
             → "Nessun caso trovato. Inserisci codice guasto."
```

---

## 8. Full End-to-End Flow — Real Example

**Message:** `"ho una FIAT Ducato diesel del 2001, spia motore accesa"`

```
┌────────────────────────────────────────────────────────────┐
│  FRONTEND                                                   │
│  "ho una FIAT Ducato diesel del 2001, spia motore accesa"  │
└────────────────────────┬───────────────────────────────────┘
                         │ POST /api/chat/stream
                         ▼
┌────────────────────────────────────────────────────────────┐
│  CHAT SERVICE                                               │
│  Gemini detects: brand=FIAT + symptom in same message      │
│  Rule 7: identify car first, save symptom                  │
│  Calls: FindCar(brand="FIAT", model="Ducato",              │
│                 fuel="Diesel", yearFrom=2001)              │
│  Saves: original_symptom = "spia motore accesa"            │
└────────────────────────┬───────────────────────────────────┘
                         │ GET /api/vehicles?brand=FIAT...
                         ▼
┌────────────────────────────────────────────────────────────┐
│  VEHICLE SERVICE                                            │
│  SQL: SELECT * FROM vehicles                               │
│       WHERE brand ILIKE '%FIAT%'                           │
│         AND model ILIKE '%Ducato%'                         │
│         AND fuel ILIKE '%Diesel%'                          │
│         AND year_from <= 2001                              │
│         AND year_to >= 2001                                │
│  Returns:                                                   │
│    FI0370  8140.43S  2000-2002  94kw/128cv                 │
│    FI0371  RHV       2001-2002  62kw/85cv                  │
└────────────────────────┬───────────────────────────────────┘
                         │ 2 cars
                         ▼
┌────────────────────────────────────────────────────────────┐
│  CHAT SERVICE                                               │
│  Gemini: phase="identification", carMatches=[FI0370,FI0371]│
│  Streams car selection cards to frontend                   │
└────────────────────────┬───────────────────────────────────┘
                         │ SSE → car selection cards
                         ▼
┌────────────────────────────────────────────────────────────┐
│  FRONTEND                                                   │
│  Shows: [8140.43S 2000-2002] [RHV 2001-2002]               │
│  Mechanic clicks: 8140.43S                                 │
│  Auto-sends: "spia motore accesa" + confirmed_car=8140.43S │
└────────────────────────┬───────────────────────────────────┘
                         │ POST /api/chat/stream
                         │ car = {engineCode: "8140.43S"}
                         ▼
┌────────────────────────────────────────────────────────────┐
│  CHAT SERVICE                                               │
│  Gemini: SearchBySymptom(                                  │
│    symptom="spia motore accesa",                           │
│    engineCode="8140.43S"                                   │
│  )                                                          │
└────────────────────────┬───────────────────────────────────┘
                         │ GET /api/search/symptom?q=...&engine=8140.43S
                         ▼
┌────────────────────────────────────────────────────────────┐
│  SEARCH SERVICE                                             │
│                                                             │
│  ValidateSymptom("spia motore accesa")                     │
│  → 3 words, technical → Valid ✅                           │
│                                                             │
│  engineCode = "8140.43S" → car confirmed → Search Type 3  │
│                                                             │
│  Step 1 — Graph (Our PostgreSQL):                          │
│  SELECT to_id FROM graph_edges                             │
│  WHERE from_id = '8140.43S' AND relation = 'DOCUMENTED_IN'│
│  → ['199309673', '199309676', '199309714', ...]            │
│                                                             │
│  Step 2 — Embed query (Gemini API):                        │
│  "spia motore accesa" → [0.041, -0.178, 0.834, ...]       │
│                                                             │
│  Step 3 — Vector within pre-filtered set:                  │
│  SELECT id_documento, embedding <=> $vec AS dist           │
│  FROM document_embeddings                                   │
│  WHERE language = 'it'                                     │
│    AND id_documento IN ('199309673', '199309676', ...)     │
│  ORDER BY dist LIMIT 1                                     │
│  → 199309673 (GUP97383) dist=0.08                         │
│                                                             │
│  Step 4 — Fetch document from SQL Server                   │
│  Step 5 — Rule 10: count=1 → show directly                │
│                                                             │
│  Returns: { resultType: "document", documents: [GUP97383] }│
└────────────────────────┬───────────────────────────────────┘
                         │ document data
                         ▼
┌────────────────────────────────────────────────────────────┐
│  CHAT SERVICE                                               │
│  Gemini formats response:                                  │
│    phase="chat", found=true                                │
│    cases=[{ sigla: "GUP97383",                             │
│              impianto: "Iniezione",                        │
│              dispositivo: "Filtro gasolio",                │
│              causa: "Filtro gasolio ostruito",             │
│              reliability: 1 }]                             │
│  Streams SSE to frontend                                   │
└────────────────────────┬───────────────────────────────────┘
                         │ SSE
                         ▼
┌────────────────────────────────────────────────────────────┐
│  FRONTEND                                                   │
│  Renders: RepairCaseCard                                   │
│    GUP97383 — Accensione spia avaria motore (rossa)        │
│    ★ Grado di attendibilità                                │
│    Impianto: Iniezione                                     │
│    Dispositivo: Filtro gasolio                             │
│    Causa: Filtro gasolio ostruito                          │
│    Intervento: Verificare lo stato del filtro...           │
└────────────────────────────────────────────────────────────┘

Total time: ~1.5 seconds
  Vehicle Service SQL:      ~20ms
  Gemini embedding:         ~80ms
  Graph traversal SQL:       ~5ms
  Vector search SQL:        ~10ms
  SQL Server document:      ~30ms
  Gemini streaming:        ~800ms
```

---

## 9. The Validation Layer

Every call to `SearchBySymptom` passes through validation before searching.
This catches Gemini mistakes silently without showing errors to the mechanic.

### The validator

```csharp
public ValidationResult ValidateSymptom(string symptom)
{
    if (string.IsNullOrWhiteSpace(symptom))
        return ValidationResult.TooVague("Sintomo vuoto");

    var words = symptom.Trim().Split(' ',
        StringSplitOptions.RemoveEmptyEntries);

    if (words.Length < 2)
        return ValidationResult.TooVague("Sintomo troppo corto");

    // Gemini accidentally passed a fault code as symptom
    if (Regex.IsMatch(symptom.Trim(), @"^[PCBU]\d{4}$"))
        return ValidationResult.RedirectToFaultCode(symptom.Trim());

    // Only generic words, no technical content
    var stopwords = new[] { "problema", "errore", "guasto",
                             "non", "funziona", "rotto" };
    if (words.All(w => stopwords.Contains(w.ToLower())))
        return ValidationResult.TooVague("Solo parole generiche");

    return ValidationResult.Valid();
}
```

### After TooVague

```
Triggers: empty / < 2 words / only stopwords

SearchService returns immediately:
{ resultType: "vague", count: 0 }

Chat Service receives "vague":
→ Gemini formats clarification message:
  "Il problema è troppo generico.
   - Quale spia si accende?
   - Quando si manifesta?
   - Ci sono rumori anomali?
   - Hai un codice dal diagnostico?"

CRITICAL: confirmed_car is NOT cleared
  Session preserves: confirmed_car = F1AE0481C
  Next message uses: confirmed_car = F1AE0481C automatically
```

### After RedirectToFaultCode

```
Triggers: symptom = "P0504" (Gemini called wrong tool)

SearchService does NOT search symptoms.
Instead calls SearchByFaultCode internally:
  → result = SearchByFaultCodeAsync("P0504", engineCode, language)
  → returns { resultType: "document", ... }

Chat Service receives normal fault code result.
No error. No indication of wrong tool call.
Mechanic sees the correct repair document.
The routing mistake was corrected silently.
```

This is the most important case — the mechanic always gets the right
answer regardless of which tool Gemini called.

### After Valid

```
Triggers: symptom is clean and specific (2+ words, technical content)

SearchService continues to actual search:
  engineCode present? → Graph + Vector hybrid (document_embeddings)
  engineCode null?    → symptom_embeddings + Graph

Apply Rule 10 (count 0/1/2-4/5+)
Return SearchResponse to Chat Service
```

### Complete validation flow

```
ValidateSymptom(symptom)
        │
        ├── TooVague ──→ { resultType: "vague" }
        │                Chat Service: ask clarification
        │                confirmed_car: preserved ← CRITICAL
        │
        ├── RedirectToFaultCode ──→ SearchByFaultCode(code, engineCode)
        │                           Return fault code result silently
        │                           Mechanic sees correct result
        │
        └── Valid ──→ Continue to search
                      Graph + Vector (car confirmed)
                      symptom_embeddings + Graph (no car)
```

| Result | Mechanic sees | confirmed_car |
|--------|--------------|---------------|
| TooVague | Clarification questions | Preserved ✅ |
| RedirectToFaultCode | Correct fault code result | Preserved ✅ |
| Valid | Normal search result | Preserved ✅ |

---

## 10. Hallucination Protection

Hallucination happens when Gemini invents information not present in the input.
Three layers protect against this.

### Layer 1 — System prompt rules (prevent mistakes)

**Rule: Never invent fault codes**
```
REGOLA CRITICA: Non inventare mai codici guasto.
Chiama SearchByFaultCode SOLO se il meccanico ha scritto
esplicitamente un codice nel formato lettera + 4 cifre (P0504).
Se non c'è un codice esplicito → chiama SearchBySymptom.
```

**Rule: Never invent brand or model**
```
REGOLA CRITICA: Non assumere mai la marca o il modello.
Chiama FindCar SOLO con parametri scritti esplicitamente dal meccanico.
Parametri non menzionati = non passarli alla funzione.
```

**Rule: Query cleaning (Rule 11)**
```
Quando chiami SearchBySymptom, il parametro "symptom" deve contenere
SOLO la descrizione tecnica — senza parole introduttive.
Mantieni tutti i termini tecnici. Minimo 3 parole.
Se ci sono più problemi → usa il più specifico.
```

### Layer 2 — Validation in Search Service (catch mistakes)

```
Gemini invents fault code → validator detects and redirects
Gemini passes too-short symptom → validator returns TooVague
Gemini passes only stopwords → validator returns TooVague
```

### Layer 3 — Graph as ground truth (limit damage)

Even if Gemini passes a slightly wrong symptom, the graph only returns
documents explicitly connected to the confirmed car. A wrong symptom
still finds the right document because the car filter is the primary constraint.

The vector search is a ranking step WITHIN a safe pre-filtered set.
It cannot return documents for the wrong car regardless of the symptom.

### Reliability estimates by component

| Component | Reliability | Hallucination risk | Protection |
|-----------|------------|-------------------|------------|
| Gemini cleaning filler words | ~98% | Very low | Layer 1 rule |
| Gemini choosing correct tool | ~92% | Medium | Layer 1 rules |
| Gemini extracting brand/model | ~90% | Medium | Layer 1 "explicit only" rule |
| Validation layer catching mistakes | ~99% | — | Layer 2 |
| Graph limiting wrong results | ~100% | — | Layer 3 |
| Combined system | ~99.5% | Very low | All 3 layers |

### What can still go wrong

1. Gemini invents a plausible but wrong brand → FindCar returns wrong cars
   → Mechanic sees wrong options → selects wrong car → wrong document shown
   → **Mitigation:** Mechanic will recognize wrong car in selection list and retry

2. Gemini cleans away a technical term that was important
   → Symptom embedding is less precise → might find wrong document
   → **Mitigation:** Graph pre-filter (confirmed car) limits damage

3. symptom_embeddings returns wrong anomalia match
   → Wrong cars shown in selection list
   → **Mitigation:** Mechanic sees wrong cars and types a more specific symptom

All failure modes are **visible to the mechanic** and **recoverable**.
None of them crash the system. The mechanic can retry with more information.

---

## 11. Storage Numbers — Real Scale

### Embeddings storage

```
Documents:    20,000
Languages:    4
Per document: 2 embeddings (document_embeddings + symptom_embeddings)
Total rows:   20,000 × 4 × 2 = 160,000

Size per embedding: 768 floats × 4 bytes = 3,072 bytes ≈ 3 KB
Total embedding storage: 160,000 × 3 KB = 480 MB

Car embeddings: 10,000 × 3 KB = 30 MB

Total vector storage: ~510 MB
```

### Graph edges storage

```
Per document: ~12 edges average (from real GUP97380 example)
Documents:    20,000
Languages:    4 (language-specific edges)
Estimated:    ~400,000 rows

Size per row: ~150 bytes
Total: 400,000 × 150 bytes = ~60 MB
```

### Query performance

```
HNSW vector search (80,000 document_embeddings):  ~5ms
HNSW symptom search (80,000 symptom_embeddings):  ~5ms
Graph traversal (B-tree index):                   ~2ms
Validation:                                       ~0.1ms
SQL Server document fetch:                        ~30ms
Gemini embedding API:                             ~80ms
Gemini streaming:                                 ~800ms

Total end-to-end:  ~1.2–1.5 seconds
```

### Total database size estimate

```
document_embeddings:     240 MB
symptom_embeddings:      240 MB
car_embeddings:           30 MB
graph_edges:              60 MB
Indexes:                 ~80 MB

Total: ~650 MB ≈ 1 GB

A standard VPS with 4 GB RAM handles this comfortably.
PostgreSQL can keep the HNSW indexes in memory for maximum performance.
```
