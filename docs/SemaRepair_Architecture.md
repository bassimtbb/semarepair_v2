# SemaRepair Chatbot — Architecture & Rebuild Documentation

> **Status:** v2 core built and verified — production data access pending
> **Last updated:** July 2026
> **Author:** Development Team

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Prototype v1 — What Was Built](#2-prototype-v1--what-was-built)
3. [Why We Are Rebuilding](#3-why-we-are-rebuilding)
4. [The Real Scale — What We Discovered](#4-the-real-scale--what-we-discovered)
5. [GraphRAG — The New Search Approach](#5-graphrag--the-new-search-approach)
6. [New Architecture — Multi-Service](#6-new-architecture--multi-service)
7. [Technology Decisions](#7-technology-decisions)
8. [Data Access Strategy](#8-data-access-strategy)
9. [Error Handling and Resilience](#9-error-handling-and-resilience)
10. [Questions for the Company Meeting](#10-questions-for-the-company-meeting)
11. [Development Roadmap](#11-development-roadmap)
12. [Folder Structure](#12-folder-structure)
13. [Key Decisions Log](#13-key-decisions-log)

---

## 1. Project Overview

### 1.1 What is SemaRepair Chatbot

SemaRepair Chatbot is an AI-powered repair assistant for mechanics and auto
repair shops. It allows mechanics to identify their vehicle, describe a fault
or enter a diagnostic trouble code (DTC), and receive structured repair
procedures from a knowledge base of documented repair cases.

The chatbot is built on top of SemaRepair's existing database of repair guides
(GUP documents), which are structured repair cases documented by technicians
and validated by vehicle manufacturers.

### 1.2 Business Context

SemaRepair is a platform used by independent mechanics and repair shops across
Italy and Europe. Their database contains thousands of repair guides covering
vehicles across many brands. The prototype sample covers FIAT, FORD, CITROEN,
PEUGEOT, and IVECO — the full production database includes many more brands
to be confirmed at the company meeting.

Each repair guide is available in five languages:
Italian (IT), French (FR), English (EN), Portuguese (PT), and Spanish (ES).
The chatbot auto-detects the mechanic's language per message (tinyld/light,
restricted to these five candidates); no language selector is needed.

### 1.3 Current Status

| Item | Status |
|------|--------|
| Prototype v1 | ✅ Complete |
| v2 services (chat, search, vehicle, ingestion) | ✅ Built and verified against real data |
| Languages supported | ✅ IT / EN / FR / PT / ES, auto-detected per message |
| Frontend | ✅ Angular 19, light/dark theme, Tailwind v4 |
| Voice input | ✅ Three modes — Mic (Gemini transcription), Voice web (Web Speech free), Voice HD (Google Cloud TTS paid) |
| Gemini usage dashboard | ✅ Live — gemini_usage_log, /api/usage/* in search-service |
| Sample data loaded | ✅ 108 documents × 5 languages, graph + embeddings |
| Production data access | ❌ Pending company approval |
| Production hosting/SSL | ❌ Pending company decision |
| Authentication | ❌ Pending business requirements |

---

## 2. Prototype v1 — What Was Built

### 2.1 Architecture Overview

```
React Frontend (Vite + TypeScript)
        │
        │ SSE + REST
        ▼
ASP.NET Core 8 Backend (Monolith)
        │
        ├── RepairOrchestrator (Gemini function calling)
        ├── RepairPlugin (3 tools)
        ├── DocumentSearchService (vector + full-text)
        ├── CarSearchService (SQL structured)
        └── EmbeddingStartupService (background)
        │
        ▼
PostgreSQL 16 + pgvector
        │
        ├── gup_rows (2000 raw rows from Excel)
        ├── repair_documents (108 documents + embeddings)
        ├── repair_document_cars (666 car-document links)
        └── car_embeddings (157 car configs + embeddings)
        │
        ▼
Python Dataseeder (one-time load from Excel)

        + Google Gemini API
          ├── gemini-2.5-flash (chat + function calling)
          ├── gemini-embedding-001 (768-dim vectors)
          └── gemini-2.5-flash (audio transcription)
```

### 2.2 Tech Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Frontend | React + TypeScript + Vite | 18 / 5 |
| Frontend server | nginx | alpine |
| Backend | ASP.NET Core Web API | 8.0 |
| Database | PostgreSQL + pgvector | 16 |
| AI — Chat | Google Gemini 2.5 Flash | latest |
| AI — Embeddings | gemini-embedding-001 | 768 dims |
| AI — Transcription | Google Gemini 2.5 Flash | latest |
| Data loading | Python | 3.12 |
| Infrastructure | Docker + Docker Compose | latest |

### 2.3 Data Pipeline

**Phase 1 — Raw load**
Parses `GUP_PER_IA.xlsx` and loads 2000 rows into `gup_rows`.

**Phase 2 — Document extraction**
Groups by `sigla_documento`, creates 108 rows in `repair_documents`.
Extracts chapters and builds `embed_text` and `search_vector`.

**Phase 3 — Car-document links**
Creates 666 rows in `repair_document_cars`.

**Phase 4 — Car configurations**
Creates 157 rows in `car_embeddings` per unique engine code.

**Phase 5 — Embedding generation (backend)**
Generates 768-dim embeddings at startup via `gemini-embedding-001`.
Takes ~5 minutes. Fully idempotent.

### 2.4 AI Pipeline

Uses **Gemini native function calling** through `RepairOrchestrator`.

**Three tools:**
- `FindCar(brand?, model?, yearFrom?, yearTo?, fuel?, engineCode?, kw?)` — SQL
- `SearchByFaultCode(faultCode, engineCode?)` — full-text + ILIKE fallback
- `SearchBySymptom(symptom, engineCode?)` — vector similarity, topK=1

**Two-request flow:**
1. First request (no `responseMimeType`) — Gemini detects tool call
2. Tool executes against PostgreSQL
3. Second request (with `responseMimeType: "application/json"`) — streams response
4. SSE chunks sent to frontend

### 2.5 What Works

| Feature | Status |
|---------|--------|
| Natural language car identification | ✅ |
| Year, brand, model, engine code filtering | ✅ |
| Fault code search (P/C/B/U codes) | ✅ |
| Symptom-based semantic search | ✅ |
| Car selection from symptom search | ✅ |
| Repair document rendering (SemaRepair template) | ✅ |
| Voice input via Gemini transcription | ✅ |
| SSE streaming responses | ✅ |
| Multi-turn conversation | ✅ |
| Non-repair message handling | ✅ |
| Docker Compose deployment | ✅ |

### 2.6 Known Limitations

| Limitation | Root Cause |
|-----------|-----------|
| Italian only | Excel sample IT data only |
| 108 documents | Sample data |
| Semantic search returns loosely related documents | Vector similarity imprecise |
| Gemini wrong tool routing | Natural language ambiguity |
| No authentication | Prototype only |
| Excel parsing fragile | Multi-column CSV in single cell |

---

## 3. Why We Are Rebuilding

### 3.1 Problems with the Current Architecture

**Problem 1 — Semantic search is imprecise**
Vector similarity measures word meaning, not technical relationships.
Two documents can score high similarity because they both mention "motore"
even if they describe completely different systems.

**Problem 2 — No relationships between data**
No explicit knowledge that P2279 and P1102 appear together, that the EGR
valve fault affects the same system across brands, or that engine 8140.43S
appears in FIAT Ducato, CITROEN Jumper, and PEUGEOT Boxer.

**Problem 3 — Engine code filter too rigid**
Exact engine code matching only. If the confirmed engine code has no documents,
returns nothing — even when an identical engine exists under a different code.

**Problem 4 — Data quality from Excel parsing**
Fragile CSV extraction. Some documents had missing Impianto, Dispositivo,
and Causa fields.

**Problem 5 — Single language**
The real product needs IT, FR, EN, PT, ES — each requiring separate embeddings.

### 3.2 Why the Monolith Does Not Scale

| Issue | Impact |
|-------|--------|
| 40,000+ embeddings at startup | Backend unavailable for hours |
| 5 languages in same process | Slow IT search blocks FR mechanic |
| No fault isolation | One crash stops everything |
| Cannot scale search independently | Wasted resources |

---

## 4. The Real Scale — What We Discovered

### 4.1 Document Volume

| Estimate | Documents | Embeddings (5 langs) |
|----------|-----------|---------------------|
| Conservative | 5,000 | 25,000 |
| Realistic | 20,000 | 100,000 |
| Full platform | 60,000 | 300,000 |

### 4.2 Multi-Language Requirements

```
id_documento = 199309631
  ├── 199309631_IT.resx  → Italian
  ├── 199309631_FR.resx  → French
  ├── 199309631_EN.resx  → English
  ├── 199309631_PT.resx  → Portuguese
  └── 199309631_ES.resx  → Spanish
```

Embeddings generated per language. Search language-filtered before ranking.
Gemini responds in the mechanic's language.

### 4.3 Data Sources

- **Their SQL Server** — master data, all documents and vehicles
- **resx files** — `{id_documento}_{LANG}.resx` on their server
- **Excel export** — prototype only, not suitable for production

### 4.4 Impact on Architecture

| Decision | Why mandatory |
|----------|--------------|
| Multi-service | 5 languages × large document set |
| Background ingestion | 40,000+ embeddings cannot run at startup |
| Direct DB access preferred | Manual resx management at scale not feasible |
| GraphRAG | Exact relationships required |

---

## 5. GraphRAG — The New Search Approach

### 5.1 What is GraphRAG

GraphRAG connects every document to the fault codes it contains, the system
it affects, the device involved, and the vehicles it applies to — creating
a navigable network of technical knowledge that is traversed by exact SQL
queries, not similarity scores.

### 5.2 Vector RAG vs GraphRAG

| | Vector RAG | GraphRAG |
|---|---|---|
| Search method | Cosine similarity | Graph traversal |
| Finds | Documents that sound similar | Documents directly connected |
| Precision | Medium | High |
| False positives | Common | Rare |
| New fault code | Needs re-embedding | Add one graph edge |
| Cross-brand same fault | Misses if wording differs | Explicit shared edge |
| Vague query | Returns loosely related | Falls back to vector |

**Combined strategy:** GraphRAG primary, vector similarity fallback.

### 5.3 Knowledge Graph Design

```
        ┌──────────┐
        │  BRAND   │
        │  name    │
        └────┬─────┘
             │ HAS_MODEL
        ┌────▼─────┐
        │  MODEL   │
        │  name    │
        │  brand   │
        └────┬─────┘
             │ HAS_ENGINE
        ┌────▼──────────────────────────────┐
        │  CAR                              │
        │  idMacchina · engineCode          │
        │  motorizzazione · fuel            │
        │  kw · cavalli                     │
        │  yearFrom · yearTo                │
        └────┬──────────────────────────────┘
             │ DOCUMENTED_IN
        ┌────▼──────────────────────────────┐
        │  DOCUMENT                         │
        │  idDocumento · siglaDocumento     │
        │  tipoRis · title · infoDoc        │
        │  impianto · dispositivo           │
        │  anomalia · causa                 │
        │  intervento · procedura · nota    │
        │  reliability · language           │
        └────┬──────────┬──────────┬────────┘
             │          │          │
    CONTAINS_FAULT  AFFECTS_SYSTEM  INVOLVES_DEVICE
             │          │          │
        ┌────▼───┐  ┌───▼────┐  ┌──▼────────┐
        │ FAULT  │  │ SYSTEM │  │  DEVICE   │
        │ CODE   │  │ name   │  │ name      │
        │ code   │  │ categ. │  │ system    │
        │ desc   │  │        │  │ commonF.  │
        │fullText│  │        │  │           │
        └────────┘  └────────┘  └───────────┘
             │
        RELATED_TO
             │
        ┌────▼───┐
        │ FAULT  │
        │ CODE   │
        └────────┘
```

### 5.4 Node Types and Properties

#### Car
| Property | Type | Example | Source |
|----------|------|---------|--------|
| idMacchina | string | FI0370 | `ID_MACCHINA` |
| engineCode | string | 8140.43S | `CODICE_MOTORE_MACCHINA` |
| motorizzazione | string | 2.8 JTD 8v | `MOTORIZZAZIONE_MACCHINA` |
| fuel | string | Diesel | `ALIMENTAZIONE_MACCHINA` |
| kw | integer | 94 | `KW_MACCHINA` |
| cavalli | integer | 128 | `CAVALLI_MACCHINA` |
| yearFrom | integer | 2000 | `ANNO_INIZIO_MACCHINA` |
| yearTo | integer | 2002 | `ANNO_FINE_MACCHINA` |

#### Document
| Property | Type | Example | Source |
|----------|------|---------|--------|
| idDocumento | string | 199309631 | resx `<ID>` |
| siglaDocumento | string | GUP97380 | resx `<DOCNumber>` |
| tipoRis | string | GUP | resx `<TipoRis>` |
| title | string | Accensione spia avaria motore... | resx `<Titolo>` |
| infoDoc | string | FRENI \| P0504 \| C1215 \| ... | resx `<InfoDoc>` |
| impianto | string | Freni | Identificazione chapter |
| dispositivo | string | Interruttore stop | Identificazione chapter |
| anomalia | string | Accensione spia avaria motore... | Identificazione chapter |
| causa | string | Interruttore stop difettoso | Identificazione chapter |
| intervento | string | Verificare il corretto funzionamento... | Procedura chapter |
| procedura | string | - - | Procedura chapter |
| nota | string | Il componente difettoso... | Procedura chapter |
| reliability | integer | 1 / 2 / 3 | Grado chapter |
| language | string | it / fr / en / pt / es | filename suffix |

**Real examples from sample data:**

| siglaDocumento | impianto | dispositivo | anomalia (short) | causa |
|---------------|----------|-------------|-----------------|-------|
| GUP97380 | Freni | Interruttore stop | Accensione spia avaria motore | Interruttore stop difettoso |
| GUP97381 | Alimentazione carburante | Pompa alta pressione | Scarse prestazioni spia avaria | Pompa alta pressione malfunzionante |
| GUP97383 | Iniezione | Filtro gasolio | Calo prestazioni spia rossa | Filtro gasolio ostruito |
| GUP97385 | Iniezione | Candelette | Calo prestazioni spia rossa | Candelette compromesse |

#### FaultCode
| Property | Type | Example |
|----------|------|---------|
| code | string | P0504 |
| description | string | Relazione interruttore freno 1-2 |
| system | string | Freni |
| fullText | string | P0504 (Relazione interruttore freno 1-2 - Rapporto errato) |

#### System
| Property | Type | Example |
|----------|------|---------|
| name | string | Iniezione |
| category | string | Motore |

| System | Category |
|--------|----------|
| Iniezione | Motore |
| Alimentazione carburante | Motore |
| Freni | Sicurezza |
| Climatizzatore | Comfort |
| Elettronica | Elettrico |
| Cambio | Trasmissione |
| Sterzo | Telaio |

#### Device
| Property | Type | Example |
|----------|------|---------|
| name | string | Interruttore stop |
| system | string | Freni |
| commonFault | string | Interruttore stop difettoso |

#### Symptom
| Property | Type | Example |
|----------|------|---------|
| description | string | spia motore accesa scarse prestazioni |

### 5.5 Relationship Types

| From | Relationship | To | Example |
|------|-------------|-----|---------|
| Brand | HAS_MODEL | Model | FIAT → Ducato |
| Model | HAS_ENGINE | Car | Ducato → F1AE0481C |
| Car | DOCUMENTED_IN | Document | F1AE0481C → GUP97380 |
| Car | SHARES_ENGINE_WITH | Car | FIAT Ducato 8140.43S ↔ CITROEN Jumper 8140.43S |
| Document | CONTAINS_FAULT | FaultCode | GUP97380 → P0504, C1215, B1024 |
| Document | AFFECTS_SYSTEM | System | GUP97380 → Freni |
| Document | INVOLVES_DEVICE | Device | GUP97380 → Interruttore stop |
| Document | HAS_SYMPTOM | Symptom | GUP97380 → anomalia text |
| Document | HAS_TRANSLATION | Document | GUP97380_IT → GUP97380_FR |
| FaultCode | RELATED_TO | FaultCode | P0504 ↔ C1215 ↔ B1024 |
| System | CONTAINS | Device | Freni → Interruttore stop |

### 5.6 Graph Traversal Examples

#### ⚠️ Fundamental Rule — Car Must Be Confirmed Before Showing Any Document

The Search Service must **NEVER** return a full repair document unless a car
has been confirmed by the mechanic first.

```
WRONG:
  Mechanic: "spia motore accesa"
  System:   → returns GUP97389 immediately   ❌

CORRECT:
  Mechanic: "spia motore accesa"
  System:   → shows car selection list
  Mechanic: selects FIAT Ducato F1AE0481C
  System:   → returns GUP97389 for F1AE0481C   ✅
```

---

#### Query 1 — Fault code, no car confirmed
```
Mechanic: "P0504" — no car confirmed

Graph: FaultCode{P0504} ←CONTAINS_FAULT← Document ←DOCUMENTED_IN← Car
→ Return car selection list (NOT repair document)
→ Mechanic selects → THEN show repair document
```

---

#### Query 2 — Fault code with confirmed car
```
Mechanic: "P0504" — car FORD Fiesta XUJN confirmed

Graph: Car{XUJN} →DOCUMENTED_IN→ Document →CONTAINS_FAULT→ FaultCode{P0504}
→ Return documents connected to BOTH XUJN AND P0504
→ Apply Rule 10 for multiple document handling
```

---

#### Query 3 — Symptom with confirmed car (Graph + Vector hybrid)
```
Mechanic: "spia motore accesa scarse prestazioni" — F1AE0481C confirmed

Step 1: Graph — get all docs for this car:
  Car{F1AE0481C} →DOCUMENTED_IN→ [doc1, doc2, doc3, ...]

Step 2: Vector rerank WITHIN pre-filtered set:
  Embed symptom → rank by cosine distance within car's docs only
  → Return top match

Step 3: Apply Rule 10
```

---

#### Query 4 — Symptom, no car confirmed (Symptom Embeddings + Graph)

> **Why NOT full document embeddings:**
> Full document embeddings mix title, impianto, dispositivo, causa,
> intervento, procedura, nota, and fault codes into one vector.
> This dilutes the semantic meaning and causes loosely related documents
> to score high. The `anomalia` field alone is a short, precise description
> of exactly what the mechanic would describe — making it a much better
> search target.

**Gemini cleans the query first (inside the tool call):**

```
Mechanic types:
  "ho un problema con climatizzatore ventola del radiatore funzionamento continuo"

Gemini receives this and before calling SearchBySymptom, cleans it:
  Removes: "ho un problema con" (pure filler — no technical content at all)
  Keeps:   "climatizzatore ventola del radiatore funzionamento continuo"
           (the system name AND the full technical description — both are kept)

Gemini calls:
  SearchBySymptom(symptom="climatizzatore ventola del radiatore funzionamento continuo")
```

> **Why "climatizzatore" must be kept:** it is a System name, not filler. Removing
> it would discard real technical information without justification. The cleaning
> rule only removes conversational phrases ("ho un problema con", "ho notato", "c'è")
> that carry zero technical meaning — it never removes a system, device, or
> behavior description. When in doubt, Gemini keeps the word.

**System prompt rule for Gemini:**
```
Quando chiami SearchBySymptom, il parametro "symptom" deve contenere
SOLO la descrizione tecnica del problema — senza parole introduttive.

Rimuovi sempre: "ho un problema con/al/di", "c'è un problema",
"la macchina", "il veicolo", "ho", "c'è", "ho notato"

Mantieni SEMPRE i termini tecnici: nomi di sistema, dispositivo, e la
descrizione del comportamento. Il symptom deve avere almeno 3 parole tecniche.
In caso di dubbio se una parola è tecnica o filler → mantienila.

Se il messaggio descrive chiaramente DUE problemi distinti e separati,
usa il più specifico e tecnico tra i due:
  Input:  "il cambio slitta in terza e ho anche un rumore strano ai freni"
  Chiama: SearchBySymptom(symptom="cambio slitta terza marcia")
  (qui ci sono davvero due guasti diversi su sistemi diversi — si scarta
   il meno specifico, non una parola a caso dello stesso problema)
```

**Search flow after Gemini cleans the query:**

```
Step 1 — Validate symptom (see Rule 12)

Step 2 — Detect keyword type against the graph BEFORE vector search:
  Check if any part of the cleaned symptom matches a System or Device
  node exactly or closely in the graph.

  SELECT 1 FROM graph_edges
  WHERE to_type IN ('system', 'device')
    AND (to_id ILIKE '%climatizzatore%' OR to_id ILIKE '%ventola%')
    AND language = 'it';

  → "Climatizzatore" matches a System node ✅

  If a System/Device match is found:
    → Graph traversal takes priority (Search Type 2, exact match)
    → SELECT from_id FROM graph_edges
      WHERE to_id = 'Climatizzatore' AND relation = 'AFFECTS_SYSTEM'
    → Returns documents connected to this System directly — no vector needed

  If NO System/Device match is found:
    → Fall back to Step 3 (vector search on symptom_embeddings)

  This step prevents the system from skipping an exact graph match in
  favor of a vector guess when the mechanic already named a known
  System or Device.

Step 3 — Vector search on symptom_embeddings (anomalia values only)
  — used only when Step 2 finds no System/Device match, or to narrow
    results further within a matched System:

  SELECT id_documento
  FROM symptom_embeddings
  WHERE language = 'it'
  ORDER BY embedding <=> $queryVector
  LIMIT 1;

  symptom_embeddings contains ONLY anomalia values:
    "Funzionamento continuo alla massima velocità della ventola del radiatore"
    "Ventilatore abitacolo non funzionante"
    "Mancato funzionamento ventola raffreddamento"

  Query: "climatizzatore ventola del radiatore funzionamento continuo"
  → "Funzionamento continuo alla massima velocità della ventola del radiatore"
     distance = 0.06  ✅ very close match → GUP97468

  Keeping "climatizzatore" in the query does not hurt this match — GUP97468's
  Impianto field is exactly "Climatizzatore", so the extra word reinforces the
  correct result instead of diluting it.

Step 4 — Graph: extract cars from matched document:
  SELECT from_id FROM graph_edges
  WHERE to_type = 'document' AND to_id = '199309714'
    AND relation = 'DOCUMENTED_IN';
  → [FI0370, FI0371, FI0372, FI0373, FI0374, FI0375]

Step 5 — Return car selection list (NOT repair document)
  Mechanic selects → confirmed car → re-search → show document
```

---

#### Query 5 — Cross-brand same engine, engine-related, no document found
```
CITROEN Jumper 8140.43S confirmed — 0 documents found
System category: Motore → fallback ALLOWED

Fallback: Car{CI_8140.43S} →SHARES_ENGINE_WITH→ Car{FIAT_8140.43S}
          Find documents for related cars

Bot (mandatory transparency):
"Non ho trovato casi per il tuo CITROEN Jumper. Ho trovato per veicoli
 con lo stesso motore (8140.43S): FIAT Ducato, PEUGEOT Boxer.
 Le procedure potrebbero essere applicabili. Vuoi vedere?"
```

---

#### Query 6 — Non-engine system, no cross-brand fallback
```
CITROEN Jumper — "problema ai freni" — 0 documents found
System category: Sicurezza → fallback NOT ALLOWED

Reason: Brake assemblies are chassis-specific.
Sharing engine code does not mean sharing brake components.

Bot: "Non ho trovato casi per i freni sul tuo CITROEN Jumper.
     Il sistema frenante è specifico. Inserisci il codice guasto."
```

#### System Category — Engine Fallback Rules

| Category | Systems | Fallback |
|----------|---------|---------|
| Motore | Iniezione, Alimentazione carburante, Candelette | ✅ Allowed |
| Elettrico motore | Sensori motore, Gestione motore | ✅ Allowed |
| Sicurezza | Freni, ABS, ESP, Airbag | ❌ Not allowed |
| Trasmissione | Cambio, Frizione | ❌ Not allowed |
| Comfort | Climatizzatore, Ventilazione | ❌ Not allowed |
| Elettrico carrozzeria | Immobilizer, Luci | ❌ Not allowed |
| Telaio | Sterzo, Sospensioni | ❌ Not allowed |

### 5.7 Graph Storage — PostgreSQL Implementation

```sql
-- Graph edge table
CREATE TABLE graph_edges (
    id          SERIAL PRIMARY KEY,
    from_type   TEXT        NOT NULL,
    from_id     TEXT        NOT NULL,
    relation    TEXT        NOT NULL,
    to_type     TEXT        NOT NULL,
    to_id       TEXT        NOT NULL,
    language    TEXT,
    weight      FLOAT       DEFAULT 1.0,
    created_at  TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX idx_graph_from     ON graph_edges (from_type, from_id);
CREATE INDEX idx_graph_to       ON graph_edges (to_type, to_id);
CREATE INDEX idx_graph_relation ON graph_edges (relation);
CREATE INDEX idx_graph_lang     ON graph_edges (language);

-- Symptom embeddings — anomalia values only, per language
-- Used for no-car symptom search entry point
-- More precise than full document embeddings for symptom matching
CREATE TABLE symptom_embeddings (
    id           SERIAL PRIMARY KEY,
    id_documento TEXT        NOT NULL,
    language     TEXT        NOT NULL,
    anomalia     TEXT        NOT NULL,
    embedding    vector(768),
    created_at   TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX idx_symptom_lang ON symptom_embeddings (language);
CREATE INDEX idx_symptom_hnsw ON symptom_embeddings
    USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);
```

**Why two separate embedding tables:**

| Table | Contains | Used for |
|-------|----------|---------|
| `document_embeddings` | Full document text (title + impianto + dispositivo + causa + intervento) | Symptom ranking WITHIN a confirmed car's documents |
| `symptom_embeddings` | Anomalia field only | Finding the right document when NO car is confirmed |

**Example rows for GUP97380:**

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
faultcode   P0504        RELATED_TO        faultcode  C1215               it
faultcode   P0504        RELATED_TO        faultcode  B1024               it
```

### 5.8 How the Ingestion Service Builds the Graph

```
For each document processed:

1. Parse resx XML
   → Extract impianto, dispositivo, anomalia, causa from Identificazione
   → Extract intervento, procedura, nota from Procedura
   → Extract fault codes from InfoDoc: regex [PCBU]\d{4}
   → Count stars in Grado chapter for reliability

2. Create Document node in PostgreSQL

3. Build graph edges:
   a. Car → DOCUMENTED_IN → Document
   b. Document → CONTAINS_FAULT → FaultCode (each code in InfoDoc)
   c. FaultCode → RELATED_TO → FaultCode (all pairs in same document)
   d. Document → AFFECTS_SYSTEM → System (from impianto)
   e. Document → INVOLVES_DEVICE → Device (from dispositivo)
   f. Document → HAS_TRANSLATION → Document (link IT/FR/EN/PT/ES)
   g. Car → SHARES_ENGINE_WITH → Car (same engineCode, different brand)

4. Generate full document embedding → store in document_embeddings
   embed_text = sigla + titolo + impianto + dispositivo + anomalia + causa

5. Generate anomalia embedding → store in symptom_embeddings
   embed_text = anomalia field ONLY
   → shorter, more precise, better for symptom entry point search
```

### 5.9 Why This Solves the Current Problems

| Problem in v1 | GraphRAG Solution |
|---------------|-------------------|
| Loosely related documents returned | Graph traversal returns only connected documents |
| Ventilatore abitacolo for ventola radiatore | Different System nodes — no shared edge |
| No fault code relationships | RELATED_TO edges connect co-occurring codes |
| Engine code filter too rigid | SHARES_ENGINE_WITH finds documents across brands |
| Missing Impianto/Dispositivo | Fields from resx XML directly — no extraction |
| Full document embeddings imprecise for symptoms | symptom_embeddings uses anomalia only |
| Filler words diluting query embeddings | Gemini cleans query before calling SearchBySymptom |
| Single language only | Language filter on all tables |
| Document shown before car confirmed | Business rules in 5.10 enforce car-first |

### 5.10 Business Rules — Conversation Flow

---

**Rule 1 — Car confirmation mandatory before showing any repair document**

```
IF confirmed_car IS NULL AND about to return repair document
THEN → STOP → return car selection list → wait for confirmation
```

---

**Rule 2 — Symptom without car → show car selection list**

```
IF symptom AND confirmed_car IS NULL
THEN → vector search on symptom_embeddings
     → graph: extract cars from best document
     → return car selection list (NOT repair content)
```

---

**Rule 3 — Fault code without car → show car selection list**

```
IF fault code AND confirmed_car IS NULL
THEN → graph: find cars with this fault
     → return car selection list (NOT repair content)
```

---

**Rule 4 — Car confirmation message always shown**

```
Mechanic selects car → confirm before proceeding
"Veicolo confermato: FIAT Ducato 2.3 JTD 16v (F1AE0481C).
 Descrivi il problema o inserisci un codice guasto."
```

Car badge visible in header at all times.

---

**Rule 5 — Confirmed car persists for entire session**

```
Message 1: "ho un FIAT Ducato F1AE0481C" → confirmed: F1AE0481C
Message 2: "spia motore accesa"          → uses F1AE0481C automatically
Message 3: "P1671"                       → uses F1AE0481C automatically
```

---

**Rule 6 — Reset clears confirmed car**

```
Mechanic clicks Reset
→ confirmed_car = null → session history cleared → fresh start
```

---

**Rule 7 — Brand + symptom in one message → identify car first**

```
Mechanic: "ho una FIAT Ducato diesel del 2004 con la spia del motore accesa"

Step 1 → FindCar(brand="FIAT", model="Ducato", fuel="Diesel", yearFrom=2004)
Step 2 → Present car options
Step 3 → Store original symptom: "spia del motore accesa"
Step 4 → Mechanic confirms car
Step 5 → Auto re-search: SearchBySymptom(saved_symptom, confirmed_engineCode)
Step 6 → Show repair document
```

---

**Rule 8 — SHARES_ENGINE_WITH requires explicit transparency**

```
NEVER: return FIAT Ducato document for CITROEN Jumper without explanation
ALWAYS: "Non ho trovato per il tuo CITROEN Jumper. Trovato per veicoli
         con lo stesso motore (8140.43S). Vuoi vedere?"
```

Only for Motore and Elettrico motore categories.

**Voice mode — frontend consent gate:** When `foundViaSharedEngine=true` is received
while voice mode is active, `VoiceModeService.handleResponseReady()` stores the full
response in `pendingSharedEngineConsent` and speaks only the disclosure message.
On the next transcript, `commitPendingTranscript()` checks this field: an affirmative
word ("sì", "yes", "oui" etc.) triggers `speakStoredConsent()` with no backend call;
any other response clears the gate and routes normally. Applies to voice mode only;
the non-voice UI shows disclosure + full card in a single turn (unchanged).

---

**Rule 9 — Vague symptom triggers clarification**

```
IF symptom ≤ 4 words AND 0 results
THEN: "Puoi descrivere meglio?
       - Quale spia si accende?
       - Quando si manifesta?
       - Ci sono rumori anomali?
       - Hai un codice dal diagnostico?"
```

---

**Rule 10 — Multiple documents found for confirmed car**

| Count | Same fault/device? | Action |
|-------|-------------------|--------|
| 0 | — | SHARES_ENGINE_WITH fallback (Motore only) or not found |
| 1 | — | Show repair document directly |
| 2–4 | YES (same DTC or device) | Show all ordered by reliability ★★★ first |
| 2–4 | NO (different systems) | Show selection list with impianto + dispositivo + causa |
| 5+ | — | Vector rerank → show top 3 + "Puoi essere più specifico?" |

---

**Rule 11 — Gemini query cleaning**

Gemini cleans the symptom inside the `SearchBySymptom` tool call.
No code filter needed — Gemini handles this naturally.

**System prompt instruction:**
```
Quando chiami SearchBySymptom, il parametro "symptom" deve contenere
SOLO la descrizione tecnica — senza parole introduttive.

Rimuovi SOLO le frasi di puro riempimento, senza contenuto tecnico:
  "ho un problema con/al/di", "c'è un problema",
  "la macchina", "il veicolo", "ho notato", "ho", "c'è"

Mantieni SEMPRE: nomi di sistema, nomi di dispositivo, e l'intera
descrizione del comportamento osservato. Minimo 3 parole tecniche.
In caso di dubbio se una parola sia tecnica o riempimento → mantienila.
Non aggiungere MAI parole che il meccanico non ha scritto.

Se il messaggio descrive chiaramente due guasti distinti e separati
(non varianti dello stesso problema) → usa il più specifico tra i due:
  Input:  "il cambio slitta in terza e sento anche rumore ai freni"
  Chiama: SearchBySymptom(symptom="cambio slitta terza marcia")
  (due sistemi diversi: si scarta il meno specifico, non si inventa nulla)

Esempi corretti — il symptom contiene SOLO parole già presenti nell'input:
  Input:  "ho un problema con climatizzatore ventola del radiatore funzionamento continuo"
  Output: SearchBySymptom(symptom="climatizzatore ventola del radiatore funzionamento continuo")
          (rimossa solo "ho un problema con" — climatizzatore è un sistema, va mantenuto)

  Input:  "la macchina ha la spia motore accesa e scarse prestazioni"
  Output: SearchBySymptom(symptom="spia motore accesa scarse prestazioni")

  Input:  "ho notato che il cambio automatico slitta in terza marcia"
  Output: SearchBySymptom(symptom="cambio automatico slitta terza marcia")
```

---

**Rule 12 — Validation layer in Search Service**

Every call to `SearchBySymptom` passes through validation before searching.
This catches Gemini mistakes silently without showing errors to the mechanic.

```
SearchBySymptom(symptom, engineCode) called
        │
        ▼
ValidateSymptom(symptom)
        │
        ├── TooVague
        │   Triggers: empty, < 2 words, only stopwords
        │   Action: return { resultType: "vague" }
        │           Chat Service asks for clarification
        │           confirmed_car NOT cleared (Rule 13)
        │
        ├── RedirectToFaultCode
        │   Triggers: symptom matches ^[PCBU]\d{4}$
        │   Action: internally call SearchByFaultCode(code, engineCode)
        │           return fault code result silently
        │           mechanic sees correct result, no error shown
        │
        └── Valid
            Action: continue to actual search
                    Graph + Vector hybrid (if car confirmed)
                    symptom_embeddings + Graph (if no car)
```

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

---

**Rule 13 — TooVague must NOT clear confirmed car**

```
Session: confirmed_car = F1AE0481C

Mechanic: "non funziona"
→ ValidationResult.TooVague
→ Ask for clarification
→ confirmed_car STAYS = F1AE0481C  ← MUST NOT be cleared

Mechanic: "spia motore accesa scarse prestazioni"
→ Search uses confirmed_car = F1AE0481C  ✅
```

---

**Full Decision Tree:**

```
Mechanic sends message
        │
        ├── Contains brand/model?
        │     YES → FindCar → car selection → confirm
        │           → save symptom → auto re-search
        │
        ├── Contains fault code (P/C/B/U + 4 digits)?
        │     Car confirmed?
        │       YES → SearchByFaultCode(code, engineCode) → Rule 10
        │       NO  → graph: cars with this fault → selection list
        │
        ├── Contains symptom description?
        │     → Gemini cleans query (Rule 11)
        │     → ValidateSymptom (Rule 12)
        │     Car confirmed?
        │       YES → Graph: docs for car → Vector rerank → Rule 10
        │       NO  → Vector on symptom_embeddings → Graph: cars → list
        │
        ├── Contains system/device/causa keyword?
        │     Car confirmed?
        │       YES → Graph: System/Device → docs for car → Rule 10
        │       NO  → Graph: cars with docs for this system → list
        │
        ├── Non-repair message?
        │     → "Sono l'assistente SemaRepair. Descrivi il problema."
        │
        └── Too vague (Rule 9)?
              → Ask for more details (confirmed_car preserved per Rule 13)
```

---

## 6. New Architecture — Multi-Service

### 6.1 Why Multi-Service is Justified

- 40,000+ embeddings need background ingestion not startup loading ✅
- 5 languages need independent search paths ✅
- Their SQL Server + our PostgreSQL = already two databases ✅
- Gemini streaming needs isolation from heavy search ✅
- Nightly sync is a background concern, not a request concern ✅

### 6.2 Service Map

```
┌──────────────────────────────────────────────────────────┐
│               Angular 19 Frontend                        │
│    Multi-language UI (IT / EN / FR / PT / ES)            │
└─────────────────────────┬────────────────────────────────┘
                          │ HTTPS
                          ▼
┌──────────────────────────────────────────────────────────┐
│                    API Gateway (nginx)                    │
│   /api/chat      → Chat Service    :5000                 │
│   /api/search    → Search Service  :5001                 │
│   /api/vehicles  → Vehicle Service :5002                 │
└──────┬─────────────────┬──────────────────┬──────────────┘
       │                 │                  │
┌──────▼──────┐  ┌───────▼──────┐  ┌───────▼──────┐
│    Chat     │  │    Search    │  │   Vehicle    │
│   Service   │  │   Service    │  │   Service    │
│   :5000     │  │    :5001     │  │    :5002     │
└──────┬──────┘  └──────┬───────┘  └──────┬───────┘
       │                │                  │
       └────────────────▼──────────────────┘
                        │
        ┌───────────────▼────────────────────┐
        │  Their SQL Server (read-only)       │
        │  Our PostgreSQL + pgvector          │
        └─────────────────────────────────────┘
                        ▲
               ┌────────┴────────┐
               │ Ingestion       │
               │ Service (nightly)│
               └─────────────────┘
```

### 6.3 Chat Service

**Responsibility:** Receive messages, orchestrate Gemini tool calls, stream SSE.

**Endpoints:**
- `POST /api/chat/stream` — SSE streaming
- `POST /api/chat/transcribe` — Audio transcription via Gemini
- `POST /api/chat/tts` — Google Cloud TTS proxy (HD voice; key never reaches browser)

**Voice input modes (frontend, three options):**
- **Mic button** — records WebM audio, calls `/api/chat/transcribe`, fills text box for mechanic review before sending
- **Voice button (🔊)** — Web Speech API, free, browser-native; Rule 8 consent gate in `VoiceModeService`
- **Voice HD button (✨)** — Google Cloud Neural2 TTS, paid; requires `GOOGLE_CLOUD_TTS_API_KEY`

**Two separate Google API keys required:**
- `GEMINI_API_KEY` — Google AI Studio (`generativelanguage.googleapis.com`), for chat + embed + transcribe
- `GOOGLE_CLOUD_TTS_API_KEY` — Google Cloud Platform (`texttospeech.googleapis.com`), for TTS only; cannot be shared with `GEMINI_API_KEY` (different credential systems)

**Dependencies:** Gemini API, Google Cloud TTS API, Search Service, Vehicle Service

### 6.4 Search Service

**Responsibility:** GraphRAG + vector search per language with validation layer.

**Endpoints:**
- `GET /api/search/fault-code?code=P2279&engine=XUJN&lang=it`
- `GET /api/search/symptom?q=ventola+radiatore&engine=F1AE0481C&lang=it`
- `GET /api/search/system?name=Iniezione&engine=F1AE0481C&lang=it`

**Response contract:**

```json
{
  "resultType": "document | car_selection | not_found | vague | redirected",
  "count": 1,
  "selectionNeeded": false,
  "redirectedTo": null,
  "documents": [
    {
      "idDocumento": "199309631",
      "siglaDocumento": "GUP97380",
      "title": "Accensione spia avaria motore",
      "impianto": "Freni",
      "dispositivo": "Interruttore stop",
      "anomalia": "Accensione spia avaria motore con veicolo funzionante",
      "causa": "Interruttore stop difettoso",
      "intervento": "Verificare il corretto funzionamento...",
      "procedura": "- -",
      "nota": "Il componente difettoso...",
      "reliability": 1,
      "language": "it",
      "dtcCodes": ["P0504", "C1215", "B1024"],
      "foundViaSharedEngine": false,
      "sharedEngineInfo": null
    }
  ],
  "cars": [],
  "validationMessage": null
}
```

> `resultType` values:
> - `document` — show repair document(s), apply Rule 10
> - `car_selection` — no car confirmed, show selection list
> - `not_found` — no documents found
> - `vague` — symptom too generic, ask for clarification
> - `redirected` — Gemini called wrong tool, silently corrected

**Dependencies:** Our PostgreSQL, Their SQL Server, Gemini Embedding API

### 6.5 Vehicle Service

**Responsibility:** Structured SQL car identification.

**Endpoints:**
- `GET /api/vehicles?brand=FIAT&model=Ducato&yearFrom=2000&yearTo=2002&fuel=Diesel`
- `GET /api/vehicles?engineCode=F1AE0481C`
- `GET /api/vehicles/{idMacchina}`

**Response contract:**

```json
{
  "count": 2,
  "cars": [
    {
      "idMacchina": "FI0370",
      "marca": "FIAT",
      "modello": "Ducato",
      "motorizzazione": "2.8 JTD 8v",
      "codiceMotore": "8140.43S",
      "alimentazione": "Diesel",
      "annoInizio": 2000,
      "annoFine": 2002,
      "kw": 94,
      "cavalli": 128
    }
  ]
}
```

### 6.6 Ingestion Service

**Responsibility:** Sync data, build graph, generate all embeddings.

**Schedule:** Nightly 02:00 AM or webhook trigger.

**Process:**
```
1. Query SQL Server for updated documents since last run
2. For each document × 5 languages:
   a. Parse resx XML
   b. Extract all fields
   c. Generate document_embedding (full embed_text)
   d. Generate symptom_embedding (anomalia only)
   e. Store both in PostgreSQL
3. Build all 7 graph edge types
4. Log to ingestion_log
```

### 6.7 API Gateway

```nginx
location /api/chat/     { proxy_pass http://chat-service:5000; }
location /api/search/   { proxy_pass http://search-service:5001; }
location /api/vehicles/ { proxy_pass http://vehicle-service:5002; }
location /              { proxy_pass http://frontend:80; }
```

Note: `POST /api/chat/tts` is covered by the existing `/api/chat/` location block — no additional nginx rule needed.

### 6.8 Data Layer

**Our PostgreSQL tables:**

```sql
-- Full document embeddings
CREATE TABLE document_embeddings (
    id           SERIAL PRIMARY KEY,
    id_documento TEXT NOT NULL,
    language     TEXT NOT NULL,
    embed_text   TEXT,
    embedding    vector(768),
    created_at   TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_doc_emb_hnsw ON document_embeddings
    USING hnsw (embedding vector_cosine_ops);

-- Anomalia-only embeddings (symptom entry point)
CREATE TABLE symptom_embeddings (
    id           SERIAL PRIMARY KEY,
    id_documento TEXT NOT NULL,
    language     TEXT NOT NULL,
    anomalia     TEXT NOT NULL,
    embedding    vector(768),
    created_at   TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_sym_emb_hnsw ON symptom_embeddings
    USING hnsw (embedding vector_cosine_ops);

-- Car embeddings
CREATE TABLE car_embeddings (
    id           SERIAL PRIMARY KEY,
    id_macchina  TEXT NOT NULL,
    engine_code  TEXT NOT NULL,
    embed_text   TEXT,
    embedding    vector(768),
    created_at   TIMESTAMPTZ DEFAULT NOW()
);

-- Graph edges
CREATE TABLE graph_edges (
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
CREATE INDEX idx_graph_from ON graph_edges (from_type, from_id);
CREATE INDEX idx_graph_to   ON graph_edges (to_type, to_id);
CREATE INDEX idx_graph_rel  ON graph_edges (relation);
CREATE INDEX idx_graph_lang ON graph_edges (language);

-- Ingestion log
CREATE TABLE ingestion_log (
    id                   SERIAL PRIMARY KEY,
    run_at               TIMESTAMPTZ DEFAULT NOW(),
    documents_processed  INTEGER,
    embeddings_generated INTEGER,
    edges_created        INTEGER,
    errors               INTEGER,
    duration_seconds     INTEGER,
    notes                TEXT
);

-- Gemini API usage and cost tracking
-- Written by: chat-service (chat_turn, transcribe), search-service (embed), ingestion-resx (embed)
-- Read by: UsageController in search-service at /api/usage/*
CREATE TABLE gemini_usage_log (
    id                  SERIAL PRIMARY KEY,
    logged_at           TIMESTAMPTZ DEFAULT NOW(),
    service             TEXT NOT NULL,
    operation           TEXT NOT NULL,
    model               TEXT,
    prompt_tokens       INTEGER,
    candidates_tokens   INTEGER,
    thoughts_tokens     INTEGER,
    total_tokens        INTEGER,
    is_estimated        BOOLEAN DEFAULT FALSE,
    cost_usd_estimated  NUMERIC(10, 6)
);
CREATE INDEX idx_usage_logged_at ON gemini_usage_log (logged_at DESC);
CREATE INDEX idx_usage_service   ON gemini_usage_log (service);
```

### 6.9 Voice Mode and Gemini Usage Dashboard

**Voice mode** (no separate service — all in Chat Service + Angular frontend):
Three input modes are provided. The Mic button records WebM audio, calls
`/api/chat/transcribe`, and fills the text input for mechanic review before
sending. The Voice web button (🔊) uses the browser-native Web Speech API (free,
no server call for TTS). The Voice HD button (✨) calls `/api/chat/tts`, which
proxies to Google Cloud Neural2 TTS — the `GOOGLE_CLOUD_TTS_API_KEY` never
reaches the browser. The Rule 8 cross-brand consent gate lives entirely in
`VoiceModeService` on the frontend (see §5.10 Rule 8).

**Gemini usage dashboard** (no separate service — added to Search Service):
Three places that call Gemini write rows to `gemini_usage_log`:
`GeminiChatClient` in chat-service, `QueryEmbedder` in search-service, and
`embedder.py` in ingestion-resx. Three read-only endpoints were added to
`UsageController` / `UsageQueryService` in **search-service**:
- `GET /api/usage/summary` — total tokens + estimated cost by service
- `GET /api/usage/by-operation` — breakdown by operation type
- `GET /api/usage/log` — paginated raw log

The Angular frontend has a `/usage` route (shared-secret gated) that fetches
these endpoints and renders daily token/cost charts. `thoughtsTokenCount` from
Gemini's `usageMetadata` is tracked separately in `thoughts_tokens`; it is
billed differently from candidate tokens and was the source of an undercount
bug that is now fixed.

---

## 7. Technology Decisions

### 7.1 Why ASP.NET Core
Same stack as v1, proven working, strong typing, EF Core + Npgsql,
first-class Docker support.

### 7.2 Why Python for Ingestion
Best XML/data processing ecosystem, familiar from v1 dataseeder,
simpler for scheduled background jobs.

### 7.3 Why PostgreSQL + pgvector
Already in v1, HNSW index for fast similarity, graph edges as simple table,
full-text search built in, free and open source.

### 7.4 Why Gemini
Already integrated, `gemini-2.5-flash` fast and cost-effective, native
function calling, `gemini-embedding-001` 768-dim, supports IT/FR/EN/PT/ES.
One `GEMINI_API_KEY` (Google AI Studio) covers chat + embed + transcribe.
A separate `GOOGLE_CLOUD_TTS_API_KEY` (Google Cloud Platform, different
credential system) is required for Voice HD TTS only. These two keys cannot
be merged — they authenticate to different Google APIs.

### 7.5 Why Docker Compose
Already in v1, multi-service without Kubernetes complexity, simple VPS
deployment, health checks between services.

---

## 8. Data Access Strategy

### 8.1 Their SQL Server (read-only)

```
Server={host};Database={db};User Id={readonly_user};
Password={pwd};TrustServerCertificate=True;
```

Tables needed: Documents, Chapters, Vehicles, Vehicle-Document mapping.

### 8.2 Our PostgreSQL (write)

Stores only what we generate. Never stores copies of their repair content.

Full table definitions are in Section 6.8.

### 8.3 Multi-Language Embeddings

```
Document 199309631:
  IT document_embedding → full embed_text in Italian
  IT symptom_embedding  → anomalia in Italian only
  FR document_embedding → full embed_text in French
  FR symptom_embedding  → anomalia in French only
  EN document_embedding → full embed_text in English
  EN symptom_embedding  → anomalia in English only
  PT document_embedding → full embed_text in Portuguese
  PT symptom_embedding  → anomalia in Portuguese only
  ES document_embedding → full embed_text in Spanish
  ES symptom_embedding  → anomalia in Spanish only
```

Total embeddings per document: 10 (5 languages × 2 tables).

### 8.4 Ingestion Schedule

```
Trigger 1 — Nightly at 02:00 AM
  Process all documents updated since last run
  Estimated: 30–60 minutes incremental

Trigger 2 — Webhook (optional)
  Company notifies when new documents published

Trigger 3 — Manual
  Admin endpoint for forced full re-ingestion
```

---

## 9. Error Handling and Resilience

### 9.1 Gemini API Unavailable

```
Chat Service:
  → Retry 3 times (1s, 2s, 4s backoff)
  → If all fail: "Il servizio AI è temporaneamente non disponibile."

Ingestion Service:
  → Retry embedding up to 5 times
  → Skip failed document, log error, continue
  → Report in ingestion_log
```

### 9.2 Their SQL Server Unreachable

```
Vehicle Service: retry 3 times → 503 to Chat Service
Search Service: graph traversal still works (Our PostgreSQL only)
                document content unavailable — return IDs and titles only
Ingestion: cannot run → log failure → retry next night
```

### 9.3 Our PostgreSQL Unavailable

```
Search Service: cannot search → 503 → Chat Service tells mechanic
Ingestion: cannot write → pause → retry
Chat Service: Vehicle Service still works for car identification
```

### 9.4 Individual Service Crash

```
Docker restart policy: restart: unless-stopped
Health checks: GET /health → 200 healthy / 503 degraded
nginx routes only to healthy services
Other services unaffected during restart
```

### 9.5 Ingestion Failure

```
Partial ingestion is safe — existing data remains valid
Failed documents logged in ingestion_log
Next scheduled run retries failed documents
Chatbot continues serving with existing data
```

### 9.6 Validation Errors (Search Service)

```
TooVague → return { resultType: "vague" }
           Chat Service asks clarification
           confirmed_car NOT cleared

RedirectToFaultCode → internally reroute silently
                      mechanic sees correct result

Both cases: no error shown to mechanic
            graceful degradation, not crash
```

### 9.7 Health Check Contract

```json
GET /health

200 OK (healthy):
{
  "status": "healthy",
  "service": "search-service",
  "database": "connected",
  "timestamp": "2026-07-09T10:00:00Z"
}

503 (degraded):
{
  "status": "degraded",
  "service": "search-service",
  "reason": "PostgreSQL connection failed",
  "timestamp": "2026-07-09T10:00:00Z"
}
```

---

## 10. Questions for the Company Meeting

### 10.1 Technical Questions

1. How many total repair documents (GUP) in the database?
2. How many total vehicle configurations?
3. How many new documents added per month?
4. Are all documents available in all 5 languages (IT/FR/EN/PT/ES)?
5. Can we have read-only SQL Server access?
6. Is it accessible from outside or VPN required?
7. Can you share the database schema?
8. Is there a staging database for development?
9. Is there a timestamp/change log column for incremental sync?
10. Can you implement a webhook for new document notifications?
11. Are resx files on disk or generated dynamically?
12. The resx files reference `GETFILE:{id}` — can we access images?

### 10.2 Business Questions

1. Internal use or sold to customers?
2. Who owns the embeddings we generate?
3. Are there restrictions on processing content with AI?
4. Expected monthly query volume?
5. Conversation history — store it? Where?
6. SLA expectation for response time?

### 10.3 Access Request Template

```
Oggetto: Richiesta accesso dati — SemaRepair AI Chatbot

OPZIONE 1 — Accesso diretto al database:
  • SQL Server read-only, utente con SELECT only
  • Tabelle: documenti, veicoli, capitoli, traduzioni

OPZIONE 2 — Export bulk:
  • Tutti i file {id_documento}_{LANG}.resx per IT/FR/EN/PT/ES
  • Aggiornamento mensile o webhook per nuovi documenti

Garanzie:
  • Solo lettura, nessuna modifica
  • Dati non condivisi con terzi
  • Uso esclusivo per il chatbot
```

---

## 11. Development Roadmap

### 11.1 Phase 1 — Foundation ✅ Complete

- [x] Docker Compose with all service skeletons
- [x] ingestion service — xlsx → gup_rows (one-shot, 108 documents)
- [x] ingestion-resx service — resx → graph + embeddings, all 5 languages
- [x] Verify graph edges, verify symptom_embeddings, verify document_embeddings

### 11.2 Phase 2 — Backend Services ✅ Complete and verified

- [x] Vehicle Service — SQL filtering, 5 languages, health check
- [x] Search Service — GraphRAG + vector hybrid, validation layer, all 13 business rules, language filtering, Rule 10
- [x] Chat Service — Gemini function calling, SSE streaming, 5 languages, transcription, system prompt with Rule 11, all business rules
- [x] API Gateway — nginx routing, all services wired

### 11.3 Phase 3 — Frontend ✅ Complete

- [x] Angular 19 standalone components, Tailwind v4 CSS-first
- [x] Light/dark theme via `.dark` class on `<html>`
- [x] Language auto-detection per message (tinyld/light)
- [x] All chat components: car selection, repair document card, message bubbles
- [x] Gemini usage dashboard at `/usage`

### 11.4 Phase 4 — Voice Mode ✅ Complete, Phase 1 gate verified

- [x] Mic button (record → Gemini transcription → fill text box)
- [x] Voice web button (🔊) — Web Speech API, free
- [x] Voice HD button (✨) — Google Cloud Neural2 TTS, paid, backend proxy
- [x] Rule 8 frontend consent gate in `VoiceModeService` — live-tested with Playwright (7/7 PASS)
- [x] `isAffirmativeConsent()` multilingual: IT/EN/FR/PT/ES

### 11.5 Phase 5 — Gemini Usage Dashboard ✅ Complete

- [x] `gemini_usage_log` table written by 3 services
- [x] `thoughtsTokenCount` tracked separately (billing fix)
- [x] `/api/usage/*` endpoints in search-service
- [x] Angular dashboard at `/usage`

### 11.6 Pending — blocked on company meeting / business decisions

- [ ] Production data access (SQL Server read-only or resx export)
- [ ] Full document set (estimated 5,000–60,000 real documents)
- [ ] Authentication (API key / JWT / SSO — pending business requirements)
- [ ] Session persistence (Redis / Postgres — currently in-memory)
- [ ] Production hosting, SSL certificate
- [ ] Update mechanism (nightly cron vs webhook — pending company meeting)
- [ ] Image handling (`GETFILE:{id}` references in resx — pending company meeting)

---

## 12. Folder Structure

### 12.1 Repository Layout

```
semarepair_v2/
├── services/
│   ├── chat/
│   ├── search/
│   ├── vehicle/
│   ├── ingestion/         ← xlsx → gup_rows (one-shot)
│   └── ingestion-resx/    ← resx → graph + embeddings (one-shot)
├── frontend/              ← Angular 19 (standalone components, Tailwind v4)
├── nginx/
│   └── nginx.conf
├── docs/
│   ├── SemaRepair_Architecture.md
│   ├── SemaRepair_Architecture_IT.md
│   ├── VoiceMode_Architecture_v2.md
│   └── log-dashboard.md
├── docker-compose.yml
├── docker-compose.override.yml
├── .env.example
├── .env
├── progress.md
└── README.md
```

### 12.2 Service Structure

```
services/search/
├── Controllers/
│   └── SearchController.cs
├── Services/
│   ├── GraphSearchService.cs
│   ├── VectorSearchService.cs
│   ├── SymptomSearchService.cs    ← uses symptom_embeddings
│   └── ValidationService.cs      ← TooVague / Redirect / Valid
├── Models/
│   ├── SearchRequest.cs
│   ├── SearchResponse.cs
│   └── ValidationResult.cs
├── Program.cs
└── Dockerfile
```

### 12.3 Docker Compose

```yaml
services:

  nginx:
    image: nginx:alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx/nginx.conf:/etc/nginx/conf.d/default.conf
    depends_on:
      - chat-service
      - search-service
      - vehicle-service
      - frontend

  frontend:
    build: ./frontend
    restart: unless-stopped

  chat-service:
    build: ./services/chat
    restart: unless-stopped
    environment:
      GEMINI_API_KEY: ${GEMINI_API_KEY}
      GOOGLE_CLOUD_TTS_API_KEY: ${GOOGLE_CLOUD_TTS_API_KEY}
      SEARCH_SERVICE_URL: http://search-service:5001
      VEHICLE_SERVICE_URL: http://vehicle-service:5002

  search-service:
    build: ./services/search
    restart: unless-stopped
    environment:
      OUR_DB: ${OUR_DB_CONNECTION}
      THEIR_DB: ${THEIR_DB_CONNECTION}
    depends_on:
      our-postgres:
        condition: service_healthy

  vehicle-service:
    build: ./services/vehicle
    restart: unless-stopped
    environment:
      THEIR_DB: ${THEIR_DB_CONNECTION}

  ingestion:
    build: ./services/ingestion
    restart: "no"
    environment:
      THEIR_DB: ${THEIR_DB_CONNECTION}
      OUR_DB: ${OUR_DB_CONNECTION}
    depends_on:
      our-postgres:
        condition: service_healthy

  ingestion-resx:
    build: ./services/ingestion-resx
    restart: "no"
    environment:
      OUR_DB: ${OUR_DB_CONNECTION}
      GEMINI_API_KEY: ${GEMINI_API_KEY}
    depends_on:
      our-postgres:
        condition: service_healthy

  our-postgres:
    image: pgvector/pgvector:pg16
    restart: unless-stopped
    environment:
      POSTGRES_DB: ${POSTGRES_DB}
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - ./data/postgres:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER}"]
      interval: 5s
      timeout: 5s
      retries: 10
```

---

## 13. Key Decisions Log

### 13.1 Decisions Made

| Decision | Choice | Reason |
|----------|--------|--------|
| Search approach | GraphRAG + Vector fallback | Graph precision + vector flexibility |
| Symptom search entry point | symptom_embeddings (anomalia only) | More precise than full document embeddings |
| Query cleaning | Gemini cleans inside tool call | Handles all languages and phrasings without code |
| Validation layer | TooVague / Redirect / Valid in Search Service | Silent correction of Gemini mistakes |
| Architecture | Multi-service | Scale, 5 languages, background ingestion |
| Chat technology | ASP.NET Core 8 | Same as v1, proven |
| Ingestion technology | Python | Best XML/data ecosystem |
| Vector database | PostgreSQL + pgvector | Already in stack |
| Graph storage | PostgreSQL graph_edges table | No separate graph DB at this scale |
| AI provider | Google Gemini | Already integrated, all 5 languages |
| Infrastructure | Docker Compose | Sufficient for VPS |
| Car confirmation | Mandatory before any document | Prevents wrong engine procedure |
| Cross-brand fallback | Engine-related systems only | Chassis-specific systems differ per brand |
| Confirmed car on TooVague | Preserved | Mechanic should not re-confirm after vague message |
| Frontend framework | Angular 19, not React | Fresh build from scratch; v1 React prototype not reused |
| Languages | 5 (IT/FR/EN/PT/ES) | Real resx sample data includes Spanish; no reason to discard it |
| Language detection | tinyld/light per message, restricted to 5 candidates | Auto-detect eliminates language UI; /light stays under 500 KB bundle budget |
| Voice input | Three modes: Mic / Web Speech (free) / Google Cloud TTS (paid HD) | Separate quality tiers; TTS key never reaches browser — backend proxy only |
| Voice Rule 8 gate | Frontend `pendingSharedEngineConsent` in `VoiceModeService` | Backend always returns causa/intervento on Turn 1 (correct for non-voice UI); Gemini does not re-search after "sì" — frontend gate eliminates both backend calls on consent |
| Gemini usage tracking | gemini_usage_log table + /api/usage/* in search-service | No separate service; thoughtsTokenCount tracked separately (billed differently) |
| Dark mode | `.dark` class on `<html>` (Tailwind v4 class strategy); `:host-context(.dark)` in component CSS | prefers-color-scheme and data-theme do not apply; explicit toggle persisted to localStorage is the only source of truth |

### 13.2 Decisions Pending

| Decision | Options | Blocked by |
|----------|---------|-----------|
| Data access method | Direct SQL Server / resx export / API | Company meeting |
| Total document count | 5,000 / 20,000 / 60,000 | Company meeting |
| Vehicle brands in scope | Prototype: 5 / Full DB: unknown | Company meeting |
| Authentication | None / API key / JWT | Business requirements |
| Conversation history | Store / not store | Business + legal |
| Hosting | VPS / cloud / company server | Business |
| Update mechanism | Nightly cron / webhook | Company meeting |
| Image handling | Include / exclude | Company meeting |

---

*Document version 4.0 — July 2026*
*Next review: After company meeting / production deployment decision*
