# SemaRepair v2 — Progress Log

> Purpose of this file: hand-off context for another Claude session (or any
> new collaborator) to understand what exists, why it's built this way, and
> what's left. Read this alongside `docs/SemaRepair_Architecture.md` (full
> design) and `docs/EmbeddingAndGraph_Technical.md` (embedding/graph deep
> dive) — those two are the source of truth for the *target* design. This
> file describes the *current, actually-built* state.

---

## 1. What SemaRepair v2 is

An AI repair assistant chatbot for car mechanics. Given a vehicle and a
fault description (symptom or DTC code), it returns a structured repair
procedure from a knowledge base of ~108 sample repair documents (GUP
documents), each available in 5 languages (IT/FR/EN/PT/ES).

It is being rebuilt from a v1 prototype (monolith, Italian-only, pure
vector search) into a GraphRAG architecture: a `graph_edges` table
(Car→Document→FaultCode/System/Device relationships) is the primary
search path, with vector similarity used only as a fallback/reranker
within graph-filtered results. This solves v1's main problem — vector
search returning "loosely related but wrong" documents.

Real production data access (their SQL Server, full document count,
brand list) is **pending a company meeting** — everything here is built
against local sample data (540 `.resx` files + `GUP_PER_IA.xlsx`) so the
pipeline is ready to point at the real source once access is granted.

---

## 2. Architecture decision (agreed, not yet fully built)

Full 4-service split, per `docs/SemaRepair_Architecture.md` section 6,
plus a second ingestion service we split out ourselves:

```
nginx (API gateway) → chat-service (:5000) → calls search-service + vehicle-service
                    → search-service (:5001) — GraphRAG + vector, validation layer
                    → vehicle-service (:5002) — structured SQL car lookup
ingestion (one-shot)       — xlsx -> gup_rows (vehicle<->document mapping)
ingestion-resx (one-shot)  — resx files -> graph_edges + document_embeddings + symptom_embeddings
```

The doc's section 6.6 describes a single Ingestion Service. We built it as
**two** one-shot services instead: `ingestion` (xlsx, simple/fast, no
external API) and `ingestion-resx` (resx parsing + graph + embeddings,
needs the Gemini API and depends on `ingestion` having run first). This
wasn't a deliberate architectural deviation from the doc so much as how
the work was sequenced — worth knowing if a future session expects one
ingestion service and finds two.

**Ingestion is now fully functional end-to-end** (xlsx → graph → real
embeddings). **search**, **vehicle**, and **chat** are all now fully
implemented and verified against live data (see section 6) — the full
roadmap order (Vehicle → Search → Chat) is complete. What's left is
infrastructure wiring (chat-service into `docker-compose.yml`, nginx,
frontend) and the items still pending the company meeting (section 9).

---

## 3. Current repo structure

```
semarepair_v2/                       (root folder name is lowercase on disk;
                                       doc's tree shows "SemaRepair-v2" —
                                       not renamed, flagged as a known gap)
├── services/
│   ├── chat/             — ASP.NET Core 8, WORKING — Gemini orchestration + SSE
│   ├── search/           — ASP.NET Core 8, WORKING — GraphRAG + vector search
│   ├── vehicle/           — ASP.NET Core 8, WORKING — structured SQL car lookup
│   ├── ingestion/         — Python, WORKING — xlsx -> gup_rows loader
│   └── ingestion-resx/    — Python, WORKING — resx -> graph + embeddings
├── frontend/           — empty placeholder, nothing built
├── nginx/
│   └── nginx.conf      — documented routing config, NOT wired into docker-compose
├── docs/
│   ├── SemaRepair_Architecture.md       — full target design
│   └── EmbeddingAndGraph_Technical.md   — embedding/graph deep dive
├── Data/
│   ├── GUP_PER_IA.xlsx          — source spreadsheet (vehicle<->document mapping)
│   └── resx_samples/            — 540 .resx files (108 docs x 5 langs) - fully ingested
├── pgdata/              — Postgres data volume (gitignore-worthy, see section 7)
├── docker-compose.yml           — our-postgres + ingestion + ingestion-resx + search-service + vehicle-service
│                                   (chat-service is WORKING but NOT YET added here - see section 7)
├── docker-compose.override.yml  — local-dev-only port exposure (postgres, search, vehicle)
├── .env / .env.example          — includes GEMINI_API_KEY (real key is in .env, not committed anywhere else)
├── README.md
└── progress.md          — this file
```

---

## 4. Ingestion service (xlsx → gup_rows) — what's actually working

**Files:** `services/ingestion/{parser.py, seeder.py, schema.sql, Dockerfile, requirements.txt}`

**What it does:** parses `Data/GUP_PER_IA.xlsx` and loads a vehicle↔document
mapping table (`gup_rows`) into Postgres. Runs automatically as part of
`docker compose up` (one-shot container, `restart: "no"`, depends on
`our-postgres` being healthy). **Idempotent**: checks `SELECT COUNT(*) FROM
gup_rows` first and skips loading entirely if already populated.

**The xlsx is malformed in a specific, now-handled way:**
- Every data row's 16 logical CSV fields (`ID_MACCHINA` ... `CONTENUTO_DOCUMENTO`)
  are packed as comma-separated text into a single Excel cell, not real
  Excel columns.
- When the title field (`TITOLO_DOCUMENTO`) itself contains a comma, Excel
  splits the row across 2–3 actual columns instead of one. This looked
  like data truncation at first (297/2000 rows appeared cut off) until we
  confirmed it was a column-split artifact, not real data loss.
- Some accented characters arrived as **double-mojibake** (UTF-8 bytes
  misread as Latin-1, then partially "fixed" once already, producing
  `Ã ` / `ÃÂ¨`-style sequences). v1 had a working fix for this
  (`_fix_mojibake` in `parser.py`). **This mojibake is specific to the xlsx
  export** — the resx files (section 5) are clean UTF-8 and need no such fix.

**Scope decision:** `gup_rows` holds **vehicle attributes + `id_documento`
only** — `sigla_documento`, `titolo_documento`, `parole_chiave`,
`capitolo_documento`, `contenuto_documento` were deliberately dropped.
Document content (title, chapters, anomalia/causa, fault codes) is parsed
**separately, directly from the `.resx` files** (section 5) — the xlsx is
only used for the vehicle↔document relationship. The parser **dedupes**
on `(id_macchina, id_documento)` (the source data repeated each pair once
per document chapter — 3x on average).

**Verified numbers (match `docs/SemaRepair_Architecture.md` exactly):**
2000 raw Excel rows → 666 unique vehicle↔document rows, 108 unique
documents, 157 unique vehicle configs, 0 rows skipped, 1334 duplicates
correctly collapsed.

**Table schema (`gup_rows`):** `id_macchina, marca_macchina,
modello_macchina, anno_inizio_macchina, anno_fine_macchina,
alimentazione_macchina, motorizzazione_macchina, kw_macchina,
cavalli_macchina, codice_motore_macchina, id_documento, created_at` +
indexes on `id_documento` and `id_macchina`.

---

## 5. Ingestion-resx service (resx → graph + embeddings) — what's actually working

**Files:** `services/ingestion-resx/{resx_parser.py, graph_builder.py, embedder.py, seeder.py, schema.sql, Dockerfile, requirements.txt}`

**What it does:** parses all 540 `.resx` files, builds the 7 documented
graph edge types into `graph_edges`, and generates both embedding tables
(`document_embeddings`, `symptom_embeddings`) via the real Gemini API.
Depends on `services/ingestion` having already populated `gup_rows`
(`depends_on: ingestion: condition: service_completed_successfully` in
docker-compose) because two of the seven edge types come from there.

### 5.1 resx parsing — key findings

- Real file structure: `<dataroot><XDOCUMENTO>...<XCAPITOLO>...</XCAPITOLO></XDOCUMENTO></dataroot>`
  (an Access/InfoPath-style XML export, not .NET's native resx key/value
  format, despite the `.resx` extension). Matches the doc's illustration
  closely, with extra real-world fields (`XTIPORIS`, `XFILE`, `RifIDFileICO`, etc.).
- **Field labels are translated per language** ("Impianto:" / "Système :" /
  "System:" / "Sistema:" / "Sistema:"), but **chapter and field order is
  positionally identical across all 5 languages** — verified against real
  samples in all 5 languages. The parser exploits this: it extracts fields
  by position (`Ordine` chapter number + label index), not by matching
  label text, so it needs no per-language label dictionary.
  - `Ordine=1` → reliability chapter; star count in the *chapter title*
    itself (e.g. `"Grado di attendibilità   (**)"` → reliability 2), not a
    separate field.
  - `Ordine=3` → identification chapter, always exactly 5 labelled segments
    in order: `[impianto, dispositivo, anomalia, autodiagnosis-errors-list, causa]`.
  - `Ordine=4` → repair procedure chapter, always exactly 3 labelled
    segments in order: `[intervento, procedura, nota]`.
- **No mojibake in resx** — confirmed by checking actual codepoints (e.g.
  `0xe9` = correct 'é', `0xe0` = correct 'à'). The `�` seen in terminal
  output was purely a console display artifact, not corrupted data. This
  is different from the xlsx export (section 4), which has real corruption.
- Fault code **descriptions** (e.g. "P0504 (Relazione interruttore freno
  1-2 - Rapporto errato)") are embedded as a `<ul><li>` list inside the
  identification chapter's 4th segment — this is the real source for the
  `FaultCode.description` property in the doc's section 5.4 node table.
  **Caveat:** the parser extracts this into
  `fault_code_descriptions: dict[code, description]` on each parsed
  record, but **nothing currently persists it** — `graph_edges` only
  stores bare string IDs (`to_id='P0504'`), and the doc's own storage
  design (section 6.8) never defines a table for fault code metadata. This
  is a real gap in the *documented* schema, not something we introduced;
  flagged here so it isn't silently lost if Search/Chat ever need to show
  a fault code's description.
- Real, expected data gaps (not parser bugs, confirmed by inspecting raw
  XML): `procedura` is the literal placeholder `"- -"` in 534/540 records
  (the field is essentially unused in practice — `intervento` carries the
  real instructions); document `199309688` has no reliability chapter at
  all in EN/ES/FR/PT (only IT has it).

### 5.2 Graph edges — what got built

Implements the 7 edges from architecture doc section 5.8 (note: section
5.5 lists 11 relationship types in its conceptual diagram, but only 7 are
actually specified as build steps in 5.8 — we only built those 7):

| Edge | Relation | Source | Per-language? |
|---|---|---|---|
| a | Car→`DOCUMENTED_IN`→Document | `gup_rows` | No |
| b | Document→`CONTAINS_FAULT`→FaultCode | regex `[PCBU]\d{4}` on `InfoDoc` | Yes |
| c | FaultCode→`RELATED_TO`→FaultCode | all pairs within same document | Yes |
| d | Document→`AFFECTS_SYSTEM`→System | `impianto` field | Yes |
| e | Document→`INVOLVES_DEVICE`→Device | `dispositivo` field | Yes |
| f | Document→`HAS_TRANSLATION`→Document | IT version → every other language version | No |
| g | Car→`SHARES_ENGINE_WITH`→Car | same `codice_motore_macchina`, any two distinct cars | No |

**Design notes/judgment calls worth knowing:**
- Edge (g): the doc's own pseudocode (section 5.8) doesn't actually filter
  by different brand — it just excludes the same car ID. We implemented it
  literally (any two distinct cars sharing an engine code get linked, same
  brand or not), inserting both directions explicitly for symmetric
  traversal. The "cross-brand" framing in the doc (section 5.6 Query 5,
  Rule 8) is about how this edge is *consumed* at search time (the
  transparency message), not a constraint on how it's built.
- Edge (f): the doc's example only links FROM the IT version of a document
  TO the other languages, treating IT as canonical (consistent with
  section 1.2, which describes IT as the base language). We followed that
  exactly, extended from 3 target languages to 4 (FR/EN/PT/ES) since we're
  processing 5 languages, not the doc's 4. If a document's IT version is
  missing, no translation edges are built for it at all (not yet observed
  in this dataset, but possible).

**Verified counts from a real run (108 documents, 540 resx files):**
```
AFFECTS_SYSTEM       532
CONTAINS_FAULT       588
DOCUMENTED_IN        666   (matches gup_rows row count exactly)
HAS_TRANSLATION      432   (108 docs x 4 target languages)
INVOLVES_DEVICE      517
RELATED_TO           426
SHARES_ENGINE_WITH   282
                    ----
Total              3,443 edges
```
Cross-checked against the doc's GUP97380 (`id_documento=199309631`) worked
example — matches structurally (same fault codes, system, device,
translation targets). One mismatch worth knowing: the doc's narrative
example says this document belongs to car `FO2983` (Ford); our actual
`gup_rows` data links it to 7 FIAT Ducato variants instead. This is the
doc's illustrative example not matching our actual local sample export —
not a bug, our pipeline correctly reflects the real data we have.

### 5.3 Embeddings — now real, not deferred

Uses the `google-genai` SDK (not the older `google-generativeai`),
`gemini-embedding-001`, explicit `output_dimensionality=768` to match the
`vector(768)` columns. `task_type="RETRIEVAL_DOCUMENT"` for both tables
(query-time embedding with `RETRIEVAL_QUERY` is a Search Service concern,
not ingestion's). Retry policy: 5 attempts with exponential backoff per
record, per architecture doc section 9.1 — a failure on one record is
logged and skipped, never aborts the whole run.

`build_document_embed_text()` follows the doc's exact formula (section 2
Step 2): `sigla + titolo + Impianto: + Dispositivo: + Anomalia: + Causa: +
Intervento: + fault_codes`, deliberately **excluding** `procedura`/`nota`
(they describe *how* to fix, which would dilute similarity with
repair-vocabulary unrelated to the actual fault). `symptom_embeddings` uses
the bare `anomalia` field only, skipped entirely for the 8 records missing
it (table has `anomalia TEXT NOT NULL`, so there's nothing to insert).

**Verified real run output:**
```
document_embeddings: 540 rows (108 docs x 5 languages)
symptom_embeddings:    532 rows (540 - 8 missing anomalia, as expected)
All embeddings: 768 dimensions, confirmed via vector_dims()
Run: 1072 embeddings generated, 0 errors, 281 seconds
```
Sample-checked `symptom_embeddings` for `199309631` across all 5 languages
— text is correctly translated and cleanly UTF-8 (Spanish "avería", French
"témoin", Portuguese "avaria" all render correctly).

### 5.4 Idempotency design

Both `ingestion` and `ingestion-resx` check existing row counts before
doing work, so a re-run of `docker compose up` does nothing once
everything is loaded — this matters a lot for `ingestion-resx` since
embeddings cost real API calls/money. The two checks in `ingestion-resx`
are **independent**, which matters:
- `graph_edges` count > 0 → skip graph rebuild (cheap, no API calls either way).
- `document_embeddings` count > 0 → skip embedding generation (expensive).
- If `GEMINI_API_KEY` isn't set, embeddings are skipped regardless, but the
  graph still builds.

This means the actual sequence we ran was: (1) run with no key → graph
builds, embeddings skipped; (2) add key, run again → graph already there
so skipped, embeddings now generate for real; (3) run again → both skipped,
confirmed zero API calls via logs. A forced full re-ingestion would need a
manual `TRUNCATE` first — there's no admin/force-reingest flag yet (the
doc's own roadmap section 8.4 calls this out as a future "Trigger 3").

**Table schema additions (`schema.sql`):** `graph_edges`,
`document_embeddings`, `symptom_embeddings`, `ingestion_log` — all created
exactly per architecture doc section 6.8 (HNSW indexes on both embedding
tables, `vector` extension enabled).

---

## 6. Search / Vehicle / Chat services

Structure (identical shape across all three, per
`docs/SemaRepair_Architecture.md` section 12.2):
```
services/<name>/
├── Controllers/<Name>Controller.cs
├── Services/<...>Service.cs
├── Models/<...>.cs
├── Program.cs
└── Dockerfile
```

### 6.1 search/ — WORKING, verified against live data

Implements all 3 documented endpoints (`fault-code`, `symptom`, `system`)
end-to-end, including car-confirmed and no-car-confirmed paths, the
validation layer (Rule 12), and the Rule 8 cross-brand fallback. Built and
tested by running the real Docker image against the live `our-postgres`
container — not just `dotnet build`.

**Services:**
- `ValidationService` — `ValidateSymptom` (TooVague / RedirectToFaultCode /
  Valid, per Rule 12). **Found and fixed a real bug in the doc's own
  specified check order**: the doc's C# runs the word-count check before
  the fault-code regex check, but a bare code (e.g. `P0504`) is always
  exactly 1 word and the regex is anchored, so `RedirectToFaultCode` was
  literally unreachable for *any* input as originally ordered. Fixed by
  checking the regex first. Confirmed against live data: `q=P0504` now
  redirects correctly, `q=rotto` still correctly returns `vague`.
- `GraphSearchService` — all graph traversal (car resolution, fault/system
  lookups, `SHARES_ENGINE_WITH` fallback, car summaries). Two real bugs
  found and fixed here, both the same root cause: `gup_rows` is one row
  per (car, document) pair, so any query joining on it without `DISTINCT`
  returns duplicates (21x and 138x observed in testing) — fixed in
  `ResolveCarIdsAsync` and `GetCarsForDocumentAsync`.
- `QueryEmbedder` — calls Gemini's `embedContent` REST API directly via
  `HttpClient` (no official Gemini .NET SDK exists), `task_type=RETRIEVAL_QUERY`,
  `output_dimensionality=768`. Verified byte-identical to a direct curl call.
- `VectorSearchService` — reranks within a graph-filtered candidate set
  (Search Type 3) using `document_embeddings`.
- `SymptomSearchService` — vector search over `symptom_embeddings`
  (Search Type 4), with an optional candidate-set restriction so it can be
  narrowed by a detected System/Device before ranking.
- `SystemCategoryLookup` — static allow-list for which System categories
  permit the Rule 8 engine-sharing fallback. Doc only gives 5 examples
  (`Iniezione, Alimentazione carburante, Candelette, Sensori motore,
  Gestione motore`); added 3 more found in real data after explicit
  user approval (`Alimentazione motore, Sistema di accensione, Sistema di
  scarico`), clearly commented as inference, not doc-sourced.
- `DocumentContentService` — added during this work, not originally
  planned: filling in document **content** (title, anomalia, causa,
  intervento, etc.) turned out to need a new `documents` table (see 6.3
  below), since neither `gup_rows` nor the embedding tables carry content.

**Two interpretive decisions made with explicit user sign-off** (the doc
under-specifies both):
- Search Type 3/4 (symptom-based) return the closest vector match(es),
  never Rule 10's 2-4/5+ document-count buckets — because unlike
  fault-code/system search, vector search has no natural "exact match
  count," so any cutoff would be invented, not specified. Rule 10
  bucketing only applies to fault-code/system search (Type 1/2), which
  does have a discrete graph-match count. **Revised since the original
  "always exactly one" version**: live testing found two documents
  (`199309673`/`199309676`) with byte-identical `anomalia` text, producing
  an exact distance tie — three such duplicate-text pairs exist in the
  108-document IT corpus. Picking one arbitrarily was silently wrong
  (it's an unstable choice, not a ranking result), so `SearchController`
  now has a `TieThreshold` constant (0.02 cosine distance): Type 3 returns
  every document within that gap of the best match (not just the top 1),
  and Type 4 — which never shows document content anyway, only a
  car-selection list (Rule 1/2) — merges the car sets of every tied top
  document instead of picking one.
- Search Type 4 (symptom, no car) narrows its vector search to documents
  matching a detected System/Device when one is found in the symptom
  text, per the doc's narrative description — even though the doc's own
  literal SQL example for this case doesn't show that restriction.

**`SearchRequest` gained a `Brand` field** not in the original model:
engine codes aren't unique across brands in real data (verified: engine
`8140.43S` alone spans 14 cars across 4 brands), so `ResolveCarIdsAsync`
needed an optional brand to scope a search to one specific car/family
without collapsing Rule 8's cross-brand fallback into a no-op.

Connects to `OUR_DB`. `/health` implemented per section 9.7's contract.
Swagger UI (`Swashbuckle.AspNetCore`) added at `/swagger` — auto-generated
from the controllers/models, unconditionally enabled (not dev-gated),
since none of these services sit behind nginx yet.

### 6.2 vehicle/ — WORKING, verified against live data

Implements both documented endpoints (`GET /api/vehicles` with
brand/model/year/fuel/engineCode/kw filters, `GET /api/vehicles/{id}`).
`VehicleSearchService` queries `gup_rows` directly with `SELECT DISTINCT`
(same duplication issue as search's `GraphSearchService` — fixed from the
start here since it was already known).

**Year-range filtering is overlap, not containment**: a car matches
`yearFrom`/`yearTo` if its own production range (`annoInizio`/`annoFine`)
overlaps the query range at all, not only if it's fully contained within
it (explicit user choice — the doc's only example doesn't disambiguate
this). Verified against live data: querying `2000-2002` correctly
includes a car produced `2002-2006` (overlaps at the boundary year).

**Important caveat, unchanged from before:** the architecture doc says
Vehicle Service should query *Their SQL Server*. We don't have that
access yet, so it's wired to `OUR_DB` (`gup_rows`) for now, with a comment
flagging the swap-over point.

Verified via the real Docker image against live data: `GetById` (hit and
204 miss), engine-code search (14 distinct cars, no duplicates), and the
brand/model/year-overlap/fuel combined filter. Swagger UI also added
here, same as Search.

### 6.3 New: `documents` table (added to ingestion-resx, not originally planned)

Discovered while planning Search Service: nothing in the existing schema
held actual document **content** (title, anomalia, causa, intervento,
etc.) for Search to return — `gup_rows` deliberately excludes it (section
4) and the embedding tables only hold vectors. Added a `documents` table
to `services/ingestion-resx/schema.sql` and a `build_documents()`
populator to its `seeder.py`, keyed on `(id_documento, language)`. Cheap
to populate (no API cost), so it's part of the free/idempotent graph-build
step, not gated behind the Gemini key.

### 6.4 chat/ — WORKING, verified against live data (including real Gemini calls)

Implements both documented endpoints (`POST /api/chat/stream` SSE,
`POST /api/chat/transcribe`) end-to-end: Gemini function calling against
Search/Vehicle Service, the two-call routing→formatting pattern, in-memory
session state, and Rules 4/5/7/8/9. No formal JSON contract exists for
Chat in the docs (only the narrative `phase`/`found`/`cases` example in
the technical doc section 8) — `ChatRequest`/`ChatResponse` are still our
own design, now implemented rather than a first draft, with two real gaps
found and fixed during the work (see below).

**Files:** `services/chat/{Models/{ChatRequest,ChatResponse,GeminiTypes,Session}.cs, Services/{GeminiChatClient,ToolDefinitions,SystemPromptBuilder,SessionStore,RepairOrchestrator}.cs, Controllers/ChatController.cs}`

- `GeminiChatClient` — low-level wrapper around Gemini's `generateContent`
  REST API (no official .NET SDK, same reasoning as Search's
  `QueryEmbedder`). Verified the exact wire protocol against Google's
  current docs rather than trusting the architecture doc's v1 narrative -
  found that `gemini-2.5-flash` returns `functionCall.id: null` in
  practice (the docs describe an id meant to be echoed back), handled as
  nullable throughout. Verified live: plain text, function-call detection,
  a full function-call round-trip (sending the result back, model
  correctly used it in its final answer), JSON mode, and inline audio data
  for transcription (`GeminiPart.OfInlineData`/`GeminiInlineData`).
- `ToolDefinitions` — 4 Gemini function declarations: `FindCar`,
  `SearchByFaultCode`, `SearchBySymptom` (all 3 from v1's tool list,
  section 2.4) plus **`SearchBySystem`**, added because Search Service
  already has a tested `/api/search/system` endpoint and excluding it
  would have been an arbitrary gap. Parameter names match the doc's own
  tool-call examples (`faultCode`, `engineCode`, `symptom`). `lang` is
  deliberately not a Gemini-facing parameter - it's session/request
  config, never something to infer from conversation text.
  - **Real bug found by the build's own compiler, not by testing**: the
    `All` list property was declared *before* the 4 individual tool
    properties it referenced - C# runs static initializers top-to-bottom,
    so `All` would have silently held a list of nulls at runtime. Fixed by
    reordering.
- `SystemPromptBuilder` — `BuildRouting` (tool-routing instructions, plus
  Rule 11's exact Italian query-cleaning block ported close to verbatim,
  generalized to apply the same filler-removal principle to FR/EN/PT/ES)
  and `BuildFormatting` (the JSON-formatting call's prompt - see the
  document-content fidelity fix below for why this shrank substantially).
  Verified live against the real API: the doc's own worked symptom-cleaning
  example reproduced character-for-character, fault-code routing correctly
  wins priority over symptom text, Rule 7's brand+symptom→`FindCar` flow
  matched the doc's worked example almost exactly, English input stayed in
  English (no unwanted translation).
  - **Real bug found by live testing**: Rule 9's clarification questions
    were referenced by name ("ask the Rule 9 questions") without ever
    giving Gemini their actual text - it just echoed the raw validation
    string instead of asking anything useful. Fixed by embedding the
    literal question list.
- `Session`/`SessionStore` — in-memory (`ConcurrentDictionary`), per the
  explicit decision to skip a persistence layer for now (matches the doc's
  own unresolved Redis question, section 13). Holds `ConfirmedEngineCode`/
  `ConfirmedBrand`/`ConfirmedCarLabel` (Rules 5/8) and the full Gemini
  conversation `History`.
  - **Deliberately does NOT have a `PendingSearch`/saved-symptom field**
    for Rule 7's "re-search after car confirmation" flow, even though the
    doc's own pseudocode does this deterministically in code (save
    symptom, then explicitly replay it). We tested the riskier alternative
    live instead of assuming it: inject a purely factual synthetic turn
    ("the mechanic confirmed engine code X") into `History` with NO
    instruction to re-search, and check whether Gemini notices and
    re-issues the original tool call on its own. **15/15 trials across two
    distinct scenarios** (brand+symptom-in-one-message, and the more
    common symptom-only case) correctly re-issued the right tool with the
    original symptom text intact and the engine code/brand added - strong
    enough evidence to trust conversation history as the replay mechanism
    rather than add redundant state.
- `RepairOrchestrator` — the actual turn handler. Two-call pattern per
  turn (routing call decides the tool; a second JSON-mode call writes the
  natural-language framing), Rule 4's confirmation message (hardcoded
  per-language templates, not Gemini-generated - same sentence every time,
  not worth an API call), Rule 8/9 message phrasing.
  - **Deterministic override, not LLM trust, for structured facts**:
    `engineCode`/`brand` for an already-confirmed car are taken from
    `Session` and override whatever Gemini put in its own tool-call args -
    Chat Service already knows these authoritatively, so there's no
    reason to trust an LLM's echo of them (unlike the symptom/fault-code
    text itself, which only the mechanic's own words can supply, and which
    the Rule 7 test above proved reliable).
  - **The single biggest bug found in this service, via live testing**:
    the original design fed Gemini the *entire* raw Search Service result
    (including full document content - `intervento`, `anomalia`, etc.) and
    asked it to reproduce the relevant fields in its JSON output. Live
    testing of the Rule 7 flow showed the returned case had
    `sigla`/`impianto`/`dispositivo`/`causa`/`reliability` but **no
    `intervento`** - the actual repair instructions the mechanic needs.
    Fixed by redesigning the data flow entirely: Gemini now never sees the
    raw tool result anywhere (not in `History`, not in the formatting
    call) - `BuildResultSummary` strips it to metadata only (`resultType`,
    `count`, `foundViaSharedEngine`/`sharedEngineInfo`, `validationMessage`,
    `redirectedTo`) before it touches the conversation. `phase`/`found`/
    `cases`/`carMatches` are all parsed directly from the real Search/
    Vehicle Service JSON in code (`ParseCaseSummary`/`ParseCarOption`),
    never from Gemini's output. The fix was extended to `carMatches` too
    (not just documents), for the same fidelity reasoning, even though the
    live bug report was specifically about document content. Re-verified
    after the fix: the returned `intervento` text is byte-identical to
    `SELECT intervento FROM documents WHERE id_documento='199309631'`.
  - `ChatResponse` gained real fields that the original first-draft model
    was missing once this was actually exercised: `ChatRequest.ConfirmedBrand`
    (engine codes aren't unique across brands - same issue hit in Search
    Service), `CarOption.Marca`/`Modello`/`Motorizzazione` (the mechanic
    couldn't tell cards apart without them), `CaseSummary.IdDocumento`/
    `Intervento`/`Anomalia`/`Procedura`/`Nota`/`DtcCodes`/`Language` (the
    actual repair content - see the bug above).
- `ChatController` — `Stream` writes each `ChatResponse`
  `RepairOrchestrator` yields as its own flushed SSE `data:` event (so a
  car-confirmation message arrives before the search result that follows
  it in the same request). `Transcribe` accepts a multipart audio upload
  and calls `GeminiChatClient.TranscribeAsync`.
  - Verified live with a **real spoken WAV file** (generated via Windows
    text-to-speech) saying "I have engine warning light on, code P zero
    five zero four" - Gemini transcribed it correctly, including
    formatting the spoken digits as `P0504`.
  - **Known, unverified gap**: the browser's actual recording format
    (e.g. `MediaRecorder`'s `audio/webm`) isn't in Gemini's officially
    documented supported list (`wav`/`mp3`/`aiff`/`aac`/`ogg`/`flac`).
    Passed through as-is since there's no frontend yet to confirm what it
    will actually send.

**Not implemented / known gaps specific to Chat:**
- No retry/backoff for a failed Gemini call - section 9.1 specifies 3
  retries with 1s/2s/4s backoff before falling back to an apology message;
  currently it fails straight to the apology on the first error.
- Session state is in-memory and per-process - lost on restart, and won't
  work if Chat Service ever runs as more than one instance (same
  limitation called out for Redis in the doc's own open questions).

---

## 7. Infrastructure / docker-compose

`docker-compose.yml` now has six services: `our-postgres`
(pgvector/pgvector:pg16), `ingestion` (xlsx, depends on postgres healthy),
`ingestion-resx` (resx/graph/embeddings, depends on postgres healthy AND
`ingestion` completing successfully via
`condition: service_completed_successfully`), and two new long-running
services:
- `vehicle-service` — `restart: unless-stopped`, depends on postgres
  healthy AND `ingestion` completing (needs `gup_rows`).
- `search-service` — `restart: unless-stopped`, depends on postgres
  healthy AND `ingestion-resx` completing (needs graph + embeddings +
  `documents`), also gets `GEMINI_API_KEY` for query-time embedding.

Both connect via `OUR_DB: Host=our-postgres;Port=5432;...` — the ADO.NET
keyword format Npgsql expects, **not** the `postgresql://...` URI format
used by the Python ingestion services' `OUR_DB` (same env var name, two
different formats, because the consuming driver differs). `docker compose
up -d` is the command to run this part of the stack.

**`chat-service` is fully working (section 6.4) but NOT YET in
`docker-compose.yml`** - it was only ever built/tested as a manually-run
container on the compose network (`--network semarepair_v2_default`,
pointed at `http://search-service:5001`/`http://vehicle-service:5002` via
env vars), the same pattern used for testing search/vehicle before they
were wired in. Adding it for real is the same mechanical step already
done for search/vehicle: a `chat-service` block depending on
`search-service`/`vehicle-service` (no `condition: service_completed_successfully`
needed - they're long-running, not one-shot), `GEMINI_API_KEY`,
`SEARCH_SERVICE_URL`/`VEHICLE_SERVICE_URL` pointing at the internal
service names, plus a host port mapping in the override file.

`docker-compose.override.yml`: auto-merged by `docker compose up` (no
flag needed) — adds `our-postgres` port 5432→host (psql/DBeaver), plus
`search-service` 5001→host and `vehicle-service` 5002→host so Swagger/curl
can reach them directly from the host during development. Containers talk
to each other over the internal Docker network (`our-postgres:5432`,
etc.), not via these host mappings.

**Bug found and fixed (still relevant):** the Postgres volume was
originally `./data/postgres:/var/lib/postgresql/data`. Windows'
filesystem is case-insensitive, so `./data` silently resolved to the same
folder as the real `./Data` directory (xlsx + resx samples) — Postgres's
internal binary files were being written *inside* our actual source data
folder. Fixed by renaming the volume path to `./pgdata`. If this project
is ever moved to a case-sensitive filesystem (Linux), be aware this class
of bug won't reproduce there but also won't be caught by testing there —
keep the names distinct regardless.

**`GEMINI_API_KEY`** is now set in `.env` (real key, not a placeholder).
`.env.example` has the variable name with an empty value as documented
convention. Both `.env` files only ever existed locally in this
conversation — verify before assuming this is git-ignored if a repo is
initialized later (see section 8).

**Verified end-to-end (full stack, three consecutive runs):**
1. First run after key was added: `ingestion` skipped (already loaded),
   `ingestion-resx` skipped graph rebuild but generated 1072 embeddings
   for real (0 errors, 281s).
2. Confirmed in Postgres: row counts and vector dimensions all correct
   (section 5.3).
3. Second `docker compose up`: both services skipped everything instantly
   — confirmed via logs, zero additional API calls.

---

## 8. Key decisions log (chronological, additive to the doc's own log)

| Decision | Choice | Why |
|---|---|---|
| Architecture scope | Agree to full 4-service split now, build ingestion first | Matches doc, avoids re-deciding later |
| `gup_rows` shape | Vehicle↔document mapping only, no content fields | Document content comes from `.resx` files directly, not the xlsx |
| Excel parsing | Reuse v1's `parser.py` (mojibake fix, single-column read) | Already solved the real data quirks; reinventing would be wasted effort |
| Dedup (xlsx) | Collapse on `(id_macchina, id_documento)` | Source data repeats each pair 3x per document chapter |
| Postgres volume path | `./pgdata`, not `./data` | Case-collision bug with `Data/` on Windows — see section 7 |
| Service skeletons | Structure + real data models now, logic later (`NotImplementedException`) | Locks in contracts without jumping ahead of the Vehicle→Search→Chat build order |
| Vehicle Service DB | Points at `OUR_DB` for now, not `THEIR_DB` | We don't have production SQL Server access yet |
| Chat response model | Marked explicitly as first draft | No formal contract exists in the docs yet, unlike Search/Vehicle |
| resx languages | Process all 5 (IT/FR/EN/PT/ES), not just the doc's 4 | Real sample data includes ES; no reason to discard it |
| resx field extraction | Positional (chapter `Ordine` + label index), not per-language label text | Verified labels translate but order doesn't change across all 5 languages |
| Ingestion split | Two separate one-shot services (`ingestion`, `ingestion-resx`) instead of the doc's single Ingestion Service | Reflects how the work was actually sequenced (xlsx first, resx+graph+embeddings second, the latter gated on a Gemini key) |
| Embedding build order | Graph edges first (no external dependency), embeddings last (needs API key) | Let most of the work be built/verified immediately; embeddings were the only blocked piece |
| Embedding/graph idempotency | Independent existence checks per table, not one blanket flag | Graph rebuild is free; embeddings cost real money — must be skippable separately so adding a key later doesn't redo free work or skip the paid work |
| SHARES_ENGINE_WITH edge | Built for any two distinct cars sharing an engine code, regardless of brand | Doc's own pseudocode doesn't filter by brand; cross-brand framing is a consumption-time concern (Rule 8), not a build-time filter |
| FaultCode descriptions | Parsed and available in memory, but not persisted anywhere | The doc's own storage schema (section 6.8) has no table for this; would need a real decision (extra table?) before persisting |
| `documents` content table | Added to `ingestion-resx` (not in original schema) | Search needed somewhere to read document content from; `gup_rows` and the embedding tables don't carry it |
| `SearchRequest.Brand` | Added optional field | Engine code alone isn't unique across brands in real data (verified: one engine code spans 4 brands) |
| Search Type 3/4 result shape | Always exactly one top vector match, no Rule 10 bucketing | Vector search has no natural "exact match count" the way graph search does; any cutoff would be invented |
| Search Type 4 narrowing | Vector search narrowed to System/Device-matched docs when one is detected | Follows the doc's prose description over its literal (non-narrowing) SQL example |
| `ValidateSymptom` check order | Fault-code regex checked before word-count | Doc's own specified order made `RedirectToFaultCode` unreachable for any input — a real bug in the doc's own logic, fixed with user confirmation |
| `SystemCategoryLookup` extra entries | Added 3 systems beyond the doc's 5 examples | Found in real data, approved by user, clearly flagged as inference |
| Vehicle Service year filter | Overlap, not containment | Doc's only example doesn't disambiguate; overlap is more forgiving and matches mechanic intent better |
| Swagger | Added to Search/Vehicle, enabled unconditionally (not dev-gated) | User wanted an interactive way to see/test the APIs; neither service sits behind nginx yet, so there's no prod-exposure concern yet |
| Search/Vehicle compose wiring | Both added as real `docker-compose.yml` services with `restart: unless-stopped`, host ports via override file | No reason left to keep them out once they had real logic; matches the existing ingestion dependency-chain pattern |
| Search Type 3/4 result shape (revision) | Return all documents tied within `TieThreshold` (0.02 cosine distance) of the best match, not always exactly one | Live testing found real documents with byte-identical `anomalia` text → exact distance ties; picking one arbitrarily was an unstable, silently-wrong choice, not a real ranking result |
| Chat tool list | Added a 4th tool, `SearchBySystem`, beyond v1's 3 | Search Service already has a tested `/api/search/system` endpoint; excluding it would be an arbitrary gap |
| Chat session state | In-memory only, no persistence layer | Matches the doc's own unresolved Redis question; this is still a single-instance prototype |
| Rule 7 replay mechanism | Conversation `History` + a synthetic factual turn, NOT a deterministic `PendingSearch` field | Tested the riskier option directly instead of assuming: 15/15 live trials across two scenarios correctly re-issued the original search with the right text and engine code, with no explicit re-prompt |
| `engineCode`/`brand` in tool execution | Deterministically overridden from `Session`, never trusted from Gemini's own tool-call args | These are simple structured facts Chat Service already knows authoritatively - no reason to trust an LLM's echo of them, unlike free-text symptom/fault-code content |
| Chat document-content fidelity | Gemini never sees or produces document/car content - it's parsed directly from the raw Search/Vehicle JSON in code; Gemini only writes the short "message" framing text | Live testing found a real bug: the original design asked Gemini to reproduce document fields in its JSON output, and the result was missing `intervento` (the actual repair instructions) |
| Audio transcription mime type | Passed through as-is from the upload's `Content-Type`, no transcoding | No frontend exists yet to know what format it will actually send; verified working with a real WAV file, but browser recorders often produce `audio/webm`, which isn't in Gemini's documented supported list |

---

## 9. Known constraints / open items

**From the architecture doc itself (still unresolved, pending company meeting):**
- Total real document count (5k conservative / 20k realistic / 60k full platform)
- Full brand list (prototype only covers FIAT/FORD/CITROEN/PEUGEOT/IVECO)
- Data access method: direct SQL Server read access vs. resx export vs. webhook
- Auth model, conversation history storage, hosting target — all undecided

**From this build specifically:**
- Root folder is named `semarepair_v2` (lowercase), not `SemaRepair-v2` as
  shown in the doc's folder tree — not renamed because it risked breaking
  IDE/workspace references; flag if this matters.
- `nginx/nginx.conf` exists with the documented routing rules but is **not**
  referenced by docker-compose — it would still fail to resolve
  `chat-service`/`frontend` hostnames (those don't exist as compose
  services yet), even though `search-service`/`vehicle-service` now do
  exist and could be added to nginx's upstream today.
- `.gitignore` now exists (`pgdata/`, `services/*/bin|obj`, `.env`, etc.)
  and the project is now a git repo with commits — the real Gemini key in
  `.env` was never committed.
- No automated tests anywhere in the project yet (Search/Vehicle were
  verified manually against live Docker containers + live data, not via
  an automated test suite).
- `FaultCode.description` text is parsed from resx but has nowhere to live
  in the current schema (see section 5.1) — will need a decision (new
  table? property on an existing one?) before Search/Chat can surface it.
- The doc's section 5.5 lists 11 graph relationship types in its
  conceptual diagram; only 7 are ever specified as buildable (section
  5.8) and that's all we built. The other 4 (Brand→Model, Model→Car,
  Document→HAS_SYMPTOM→Symptom, System→CONTAINS→Device) are not
  implemented — flagging in case a future session assumes they exist.
- `chat-service` is fully built and tested but not yet a real
  `docker-compose.yml` service (section 7) — was only ever run manually
  on the compose network during testing.
- No retry/backoff for failed Gemini calls in Chat Service (section 9.1
  specifies 3 retries, 1s/2s/4s backoff) — currently fails straight to a
  fixed apology message on the first error.
- Chat's session state is in-memory/per-process — lost on restart, won't
  work if Chat Service ever scales beyond one instance.
- Audio transcription's mime-type handling is untested against a real
  browser recording (no frontend exists yet) — verified only with a
  generated WAV file; `MediaRecorder`'s typical `audio/webm` output isn't
  in Gemini's documented supported list.

---

## 10. Code review notes (self-review, for a fresh reviewer)

- **Ingestion (`parser.py`/`seeder.py`):** solid — verified against real
  data, numbers cross-checked against the architecture doc's own stats.
  Now idempotent (checks row count before loading). No retry/error
  handling around the Postgres connection itself — acceptable for a
  one-shot local script, would need it before any production cron use.
- **Ingestion-resx:** also verified against real data end-to-end,
  including a real (not mocked) Gemini API run. `generate_embeddings()`
  commits once at the end of the whole loop rather than per-record — fine
  for a one-shot batch job at this scale (540 records), but means a crash
  partway through loses the whole batch's inserts (the per-record
  try/except only protects against *that record* failing the API call,
  not a DB-level failure). Acceptable for now; would want per-record or
  chunked commits before scaling to thousands of documents.
- **Search/Vehicle:** both verified against real, live data via the actual
  Docker image (not just `dotnet build` — `dotnet run` doesn't work
  locally for these net8.0 projects since only the .NET 10 SDK is
  installed). Four real bugs found and fixed in the process, all caught
  by live-data testing rather than code review: two missing-`DISTINCT`
  duplication bugs in `GraphSearchService` (`gup_rows` joins multiply
  rows), the `ValidateSymptom` check-ordering bug (faithfully ported
  from the doc, but unreachable as originally specified), and the Type
  3/4 exact-distance-tie issue (section 6.1) — a real user-reported case
  where the API returned a different document than expected because two
  documents share identical `anomalia` text. No automated tests exist for
  either service — verification was manual curl calls against a running
  container (first a temporary test container, now the real
  `docker compose` services).
- **Chat:** verified against the real Gemini API and the real Search/
  Vehicle services throughout, not mocks - including a real spoken audio
  file for transcription. Three real bugs found and fixed during the
  work, all caught by live testing rather than code/design review: the
  `ToolDefinitions.All` static-init-order bug (would have been a silent
  list of nulls at runtime), the missing Rule 9 question text (Gemini
  just echoed the raw validation string instead of asking anything
  useful), and the big one - document content (`intervento` above all)
  going missing because the original design round-tripped it through
  Gemini's JSON output instead of splicing it from the real service
  response. No automated tests exist; no `.sln` file ties the four
  projects together.
- **No secrets at risk in shared text, but real secret now exists
  on disk:** `.env` contains a real Gemini API key as of this update —
  previously it was only dummy Postgres credentials. If this project is
  ever put under git, `.env` must be excluded from the very first commit,
  not added later.

---

## 11. Suggested next step

**All three services in the original roadmap (Vehicle → Search → Chat)
are now fully built and verified against live data** — including Chat's
Gemini orchestration, SSE streaming, and audio transcription, all tested
against the real APIs, not mocks. Nothing functionally outstanding remains
from the application logic itself; what's left is infrastructure wiring:

1. Add `chat-service` to `docker-compose.yml` (mechanically the same step
   already done for search/vehicle — see section 7 for the exact shape
   needed) so `docker compose up -d` runs the whole working stack with one
   command, chat included.
2. Point `nginx/nginx.conf` at the real `search-service`/`vehicle-service`/
   `chat-service` containers and wire it into compose — it currently only
   has the documented routing rules.
3. `frontend/` is still an empty placeholder — nothing built. This is the
   first point where the project would actually need one to be usable
   end-to-end by a mechanic, and where the audio-transcription mime-type
   gap (section 9) would get a real answer.
4. Everything gated on the company meeting (section 9: real document
   count, full brand list, production SQL Server access, auth model) is
   still pending and unblocked by nothing we can do locally.
