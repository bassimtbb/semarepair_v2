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
roadmap order (Vehicle → Search → Chat) is complete. **A frontend now
exists too** (Angular, not the doc's originally-stated React — see
section 6.4) and all six services (including **nginx** and **frontend**)
run together under one `docker compose up -d` (section 7). What's left
is the items still pending the company meeting (section 9).

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
├── frontend/           — Angular 19, WORKING — chat UI, built from scratch (not the doc's React)
├── nginx/
│   └── nginx.conf      — WORKING, wired into docker-compose — API gateway on :80
├── docs/
│   ├── SemaRepair_Architecture.md       — full target design
│   └── EmbeddingAndGraph_Technical.md   — embedding/graph deep dive
├── Data/
│   ├── GUP_PER_IA.xlsx          — source spreadsheet (vehicle<->document mapping)
│   └── resx_samples/            — 540 .resx files (108 docs x 5 langs) - fully ingested
├── pgdata/              — Postgres data volume (gitignore-worthy, see section 7)
├── docker-compose.yml           — our-postgres + ingestion + ingestion-resx + search-service + vehicle-service + chat-service + frontend + nginx
├── docker-compose.override.yml  — local-dev-only port exposure (postgres, search, vehicle, chat)
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
  planned: filling in document **content** (titolo, anomalia, causa,
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

**`SearchRequest` gained a `Marca` field** not in the original model:
engine codes aren't unique across brands in real data (verified: engine
`8140.43S` alone spans 14 cars across 4 brands), so `ResolveCarIdsAsync`
needed an optional brand to scope a search to one specific car/family
without collapsing Rule 8's cross-brand fallback into a no-op.

**Query param names are Italian, not the doc's English**
(`codiceMotore`/`marca`, not `engine`/`brand`) — a deliberate later
revision standardizing the whole Search Service contract to match Vehicle
Service's convention. Note `SearchRequest` itself was never actually
bound by the controller (its three endpoints take individual
`[FromQuery]` parameters directly) - the live, callable contract is the
controller's own parameter names, which were renamed alongside it. See
the decisions log (section 8) for the full rationale.

Connects to `OUR_DB`. `/health` implemented per section 9.7's contract.
Swagger UI (`Swashbuckle.AspNetCore`) added at `/swagger` — auto-generated
from the controllers/models, unconditionally enabled (not dev-gated); now
reachable both directly on its host port and via nginx for the `/api/*`
routes (Swagger UI itself isn't proxied, only the API endpoints are).

### 6.2 vehicle/ — WORKING, verified against live data

Implements both documented endpoints (`GET /api/vehicles` with
marca/modello/annoInizio/annoFine/alimentazione/motorizzazione/codiceMotore/kw
filters, `GET /api/vehicles/{id}`). **Query param names are Italian, not
the doc's English** (`marca` not `brand`, `codiceMotore` not `engineCode`,
etc.) — a deliberate later revision standardizing `VehicleQuery` to match
`VehicleResult`'s existing Italian convention; see the decisions log
(section 8) for the full rationale and every caller that was updated.
`VehicleSearchService` queries `gup_rows` directly with `SELECT DISTINCT`
(same duplication issue as search's `GraphSearchService` — fixed from the
start here since it was already known).

**`motorizzazione` (ILIKE, wildcarded) vs `codiceMotore` (exact match)**
are two distinct filters, not one — `motorizzazione` matches the
human-readable engine label a mechanic actually says ("1.5 TDCi 8v"),
`codiceMotore` matches `gup_rows`' internal short code ("XVJB"), which a
mechanic essentially never knows from memory. Added after a real bug: see
the decisions log (section 8) for the full repro and root cause.

**Year-range filtering is overlap, not containment**: a car matches
`annoInizio`/`annoFine` (the query's filter range) if its own production
range (`VehicleResult`'s own `annoInizio`/`annoFine`) overlaps the query
range at all, not only if it's fully contained within it (explicit user
choice — the doc's only example doesn't disambiguate this). Verified
against live data: querying `2000-2002` correctly includes a car produced
`2002-2006` (overlaps at the boundary year).

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
  own unresolved Redis question, section 13). Holds `ConfirmedCodiceMotore`/
  `ConfirmedMarca`/`ConfirmedCarId`/`ConfirmedCarLabel` (Rules 5/8) and the
  full Gemini conversation `History`.
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
  - **Car-confirmation ambiguity bug, found via a real user report with a
    full repro**: confirming a car by `engineCode`+`brand` alone is
    ambiguous - live data has engine codes shared by multiple trims of
    the same model (e.g. `8140.43S` matches 5 different IVECO Daily III
    variants: 35C-13/35S-13/40C-13/45C-13/50C-13). `ConfirmCarAsync`'s
    Vehicle Service lookup had no `ORDER BY`, so it silently returned
    whichever row came back first (`IV5018` = "35C-13") regardless of
    which trim the mechanic actually clicked - confirmed live: clicking
    "40C-13" had the bot respond "35C-13". Fixed by adding
    `ChatRequest.ConfirmedCarId` (the clicked card's `idMacchina`, already
    present in every `CarOption` from `FindCar`/search results) as the
    primary confirmation path - `ConfirmCarAsync` now looks the car up
    directly by id via `GET /api/vehicles/{id}` when given one, with
    `engineCode`/`brand` kept only as a fallback for callers without a
    specific id. Also fixed a related latent bug this surfaced: the
    "is this a *new* car confirmation" check compared `engineCode` only,
    so switching between two trims that share one engine code (e.g.
    40C-13 → 50C-13, same `8140.43S`) wouldn't even have been detected as
    a change - now compares `ConfirmedCarId` first when present. Re-tested
    live: clicking each of 40C-13/50C-13/45C-13 individually now correctly
    identifies that exact trim (not just the one trim from the bug
    report), and switching between two same-engine-code trims within one
    session is correctly detected as a new confirmation.
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
    was missing once this was actually exercised: `ChatRequest.ConfirmedMarca`
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

Swagger UI added too (same `Swashbuckle.AspNetCore` setup as Search/
Vehicle) - mainly useful for `/transcribe` (a file-upload UI works fine in
Swagger); `/stream` is SSE, which Swagger's "Try it out" doesn't render
usefully, so curl remains the practical way to test it.

**Not implemented / known gaps specific to Chat:**
- No retry/backoff for a failed Gemini call - section 9.1 specifies 3
  retries with 1s/2s/4s backoff before falling back to an apology message;
  currently it fails straight to the apology on the first error.
- Session state is in-memory and per-process - lost on restart, and won't
  work if Chat Service ever runs as more than one instance (same
  limitation called out for Redis in the doc's own open questions).

### 6.4 frontend/ — WORKING, Angular (not React), verified live in a real browser

**Deliberate deviation from the doc**: section 6.2 specifies
"React Frontend (Vite + TypeScript)". Built in **Angular 19** instead
(standalone components, signals, plain CSS) - an explicit user decision,
not a technical constraint. A complete v1 React frontend exists at
`semarepair_v2/../semarepair/SemaRepair/frontend` and was evaluated as a
reuse candidate (its presentational components map closely to v2's
`CarOption`/`CaseSummary` shapes), but the user rejected reusing it and
asked for a from-scratch Angular build instead - no v1 code was ported.

**What it does:** single-screen chat UI - message thread, car-selection
cards (from `ChatResponse.carMatches`), repair-case cards (from
`ChatResponse.cases`, with a 1-3 star reliability rating and DTC code
badges), a text input, and a mic button for voice input.

Car cards show `kw`/`cavalli` ("70 kW / 95 CV") alongside engine code and
year range - added after the user pointed out the card was missing power
info that distinguishes otherwise-identical-looking trims (e.g. two
"1.5 TDCi 8v" cards differing only in power output). `VehicleResult`
already carried these fields; they just weren't propagated through
`ChatService.Models.CarOption`/`RepairOrchestrator.ParseCarOption` or the
frontend's `CarOption` model before now.

**Built fresh against the real `ChatService.Models` contract** (not
guessed from the architecture doc, which predates Chat Service's actual
shape):
- `models/chat.models.ts` mirrors `ChatRequest`/`ChatResponse`/
  `CarOption`/`CaseSummary` field-for-field (camelCase, matching
  `JsonSerializerDefaults.Web`).
- `ChatApiService` consumes `/api/chat/stream` via `fetch` +
  `ReadableStream`, not Angular's `HttpClient` (no native SSE support) -
  parses each `data:` line as one complete `ChatResponse` JSON object,
  since the backend's SSE events are discrete messages, not a token
  stream, and there's no `[DONE]` sentinel (the connection just closes).
- `ChatStore` (signal-based service) generates one `sessionId` per
  browser tab and replays it on every request - all real session state
  (history, confirmed car) lives server-side, matching `Session.cs`'s own
  design. `confirmCar()` sends `confirmedCarId` plus `confirmedCodiceMotore`/
  `confirmedMarca` (renamed from `confirmedEngineCode`/`confirmedBrand` -
  see the consolidated Italian-naming decisions log entry) on the next
  request, matching `RepairOrchestrator`'s Rule 5/8 detection.
- Mic button records via `MediaRecorder`, posts to `/api/chat/transcribe`,
  and fills the text input with the result for the mechanic to review
  before sending (not auto-sent).

**Verified live, twice** - first via `ng serve` (dev proxy forwarding
`/api` to the real nginx gateway), then again against the actual
Docker-composed stack on port 80 - using a real Chromium browser driven
by Playwright (not a unit test): typed a real fault code, got the real 7
FIAT Ducato car-selection cards, clicked one, confirmed the header
updated with the confirmed car, and confirmed the real repair document
(`GUP97380`) rendered with byte-correct `intervento`/`nota`/DTC codes.
Zero browser console errors in either run. Full-page screenshots taken
and inspected, not just a DOM/text assertion.

**Language is now auto-detected per message, not hardcoded** - revises the
"Italian-only UI" decision below. `services/language-detector.ts` runs
`tinyld/light`'s `detectAll(text, { only: ['it','en','fr','pt','es'] })`
on the mechanic's typed text before each `sendMessage()`, restricted to
just these 5 candidates (unrestricted, against tinyld's full ~600-language
model, short technical phrases misfire - e.g. "ok grazie" matched Polish
over Italian; restricted to 5, it correctly resolves). When detection
returns nothing confident (a bare DTC code, "si", any text too short/
ambiguous to score), `ChatStore` falls back to whatever language was last
successfully detected this session (`'it'` before the first message) -
deliberately *not* re-defaulting to Italian on every short follow-up,
which would constantly fight a mechanic typing in another language.
`confirmCar()`'s synthetic "Confermo il veicolo: ..." text (hardcoded
Italian, not something the mechanic typed) is explicitly excluded from
detection - detection only runs in `sendMessage()` - otherwise clicking a
car card would silently flip the language back to Italian every time.
No dropdown/selector exists or is planned; `tinyld/light` (not the
default `tinyld` import) was chosen specifically to keep the bundle under
the 500 KB budget (full model added ~455 KB; `/light` keeps total bundle
at 284 KB) while still detecting all 5 target languages correctly on
realistic sentences in live testing.

Verified live via Playwright against the real Docker-composed stack: typed
"my engine light is on, code P0504" with no language UI touched →
request body confirmed `"language":"en"`; clicked a FIAT Ducato result →
confirmation request retained `"language":"en"` (not reset to `"it"`) →
the full repair-case response (`GUP97380` - "Engine fault warning light
illuminated...", "Check that the stop switch is working correctly...")
rendered in English end to end.

**One scope decision made by default, not yet revisited:**
- **No TTS playback** - voice *input* works end-to-end (record →
  transcribe → fill input), but spoken *replies* are out of scope since
  `chat-service` has no `/speak` endpoint (only `/transcribe`). v1 had a
  voice-mode toggle calling a TTS endpoint that doesn't exist in v2.
- The audio-transcription mime-type gap noted in section 6.3
  (`MediaRecorder`'s real `audio/webm` output vs. Gemini's documented
  list) is **still unverified** - the live browser verification above
  used the typed-text path only; no actual microphone/audio round-trip
  was exercised.

### 6.5 Frontend visual redesign (Tailwind, theming, icons, grid) — complete

A separate, presentation-only pass over the working frontend from section
6.4 — explicitly scoped to never touch `ChatStore`/`ChatApiService`/SSE
parsing/language detection, done in 4 verified stages.

**Stage 1 — Tailwind CSS v4.** Installed per Tailwind's *current* official
Angular integration, not an older v3-era guide: `tailwindcss@4`,
`@tailwindcss/postcss`, `postcss`, with a `.postcssrc.json`
(`{"plugins": {"@tailwindcss/postcss": {}}}`) and `@import 'tailwindcss';`
in `styles.css` — **no `tailwind.config.js`**, v4 is CSS-first config.
Verified `ng build`/`ng serve` both still work and every existing
component rendered unchanged (before/after screenshots) before moving on.

**Stage 2 — theme tokens.** 7 tokens (`--color-background/surface/accent/
accent-foreground/foreground/muted/border`) defined via `@theme static {
... }` (light defaults) and re-defined inside `@layer base { .dark { ...
} }` (dark values), gated by `@custom-variant dark
(&:where(.dark, .dark *));` — v4's manual class-based dark mode (no OS-
preference fallback; the toggle is the only source of truth). **Real bug
caught by inspecting compiled CSS directly**: plain `@theme` (without
`static`) tree-shakes any variable not yet referenced by a scanned
utility class, silently dropping the light-mode defaults — fixed by using
`@theme static`. **Contrast verified by calculation, not eyeballing**:
the originally-proposed dark accent `#4d7dc4` measured 4.162:1 white-text
contrast (fails WCAG AA's 4.5:1 floor); replaced with `#4773b4` (4.795:1
text, 3.723:1 accent-vs-background) after computing several candidates.

**Stage 3 — toggle + fixed input bar.** `ThemeService` (`signal`-based)
toggles `.dark` on `<html>` and persists to `localStorage`
(`semarepair-theme`); an inline `<script>` in `index.html`'s `<head>`
applies the saved class before Angular loads (no flash). Message input +
mic + send redesigned into a single fixed-to-viewport-bottom pill bar
using the new tokens. `submit()`/`toggleRecording()`/`MediaRecorder` logic
in `ChatInputComponent` was not touched — only the template/CSS.

**Stage 4 — icons, car-selection grid, full theme coverage.**
- **Icons**: `lucide-angular` (the name the user asked for) is the
  superseded/legacy package — the actively-maintained one for current
  Angular is the scoped `@lucide/angular` (flagged, then used, since it
  satisfies the actual intent). License is **ISC**, not literally MIT as
  stated — functionally equivalent permissive/no-attribution, flagged
  transparently. Standalone per-icon components (`LucideMic`,
  `LucideSend`, etc.), `<svg lucideMic [size]="18">`, color via
  `currentColor` so existing `text-*` token classes just work in both
  themes. Every icon-bearing spot found and swapped: mic, send,
  send-square (recording state), theme toggle (sun/moon), reset button
  (X), star rating. The send button had no icon at all to swap (text-only
  "Invia") — surfaced via an explicit question rather than improvised, per
  the user's "stop and tell me before improvising" instruction; user chose
  to add a Lucide send icon alongside the existing text.
- **Car-selection grid**: `message-bubble`'s car-results container changed
  from a stacked list to `display:grid; grid-template-columns:
  repeat(auto-fill, minmax(220px, 1fr))`, responsive with no breakpoint
  list. `(select)="selectCar.emit($event)"` per `app-car-card`, bound via
  `@for(...; track car.idMacchina)`, was untouched — a CSS display change
  cannot remap which card's click emits which car object. **Re-verified
  live** with the exact IVECO 5-trim regression (engine `8140.43S`, 14
  cards across 4 brands incl. 5 IVECO Daily III trims): clicking the
  "40C-13" card produces a chat confirmation reading "...IVECO Daily III
  40C-13 con motore 8140.43S..." — the grid reflow did not scramble the
  click→data binding.
- **Re-themed remaining components** off hardcoded light-only colors onto
  tokens: `car-card`, `message-bubble`, `repair-case-card`. **Contrast
  checked by calculation for the star rating and DTC badge, not
  eyeballed**: the existing filled-star amber (`#f59e0b`) measured
  2.053:1 against the light surface — fails even the lenient 3:1 floor,
  a pre-existing issue surfaced only because of the explicit check, fixed
  with per-theme Tailwind shades (`amber-700` light = 4.800:1, `amber-500`
  dark = 6.811:1). Reusing the light empty-star gray for dark mode would
  have measured 9.928:1 (too prominent, inverting filled/empty visual
  hierarchy) — fixed with `slate-500` (3.074:1). DTC badge's existing
  amber/red was checked too and found to already clear AA (6.367:1) with
  no `dark:` variant needed — documented inline rather than changed.

**A real, pre-existing layout bug found and fixed in this stage** (not
introduced by the redesign, but surfaced by it): `MessageListComponent`'s
host element defaults to `display:block` (Angular component hosts always
render as a real DOM element unless told otherwise), so `flex:1` and
`overflow-y:auto` on its *inner* template div had no effect on the *host*
— the host is the actual flex item inside `.app-shell`'s
`flex-direction:column`, and a plain block element sizes to its own
content ("auto"), never stretching to fill remaining space. Net effect:
the inner div's height was also just content-sized, `overflow-y:auto`
never triggered (`scrollHeight === clientHeight` always), and the whole
*document* scrolled instead of the message panel — confirmed via direct
`getBoundingClientRect()`/`scrollHeight` measurement (documentScrollHeight
890px > 800px viewport, list `scrollTop` never moving), not assumed from
a screenshot. This also meant the existing auto-scroll-to-bottom logic
(`el.scrollTop = el.scrollHeight` in `ngAfterViewChecked`) had been a
silent no-op all along, predating this redesign — it never had a real gap
to scroll. Fixed with `:host { display: contents; }` on
`MessageListComponent`, which removes the host from the box tree entirely
so the inner `.message-list` div becomes the *direct* flex item of
`.app-shell`, making `flex:1`/`overflow-y:auto`/`min-height:0` all work as
written. Re-verified after the fix: `documentScrollHeight === 800 ===
windowInnerHeight` (page no longer overflows), `list.scrollHeight (753) >
list.clientHeight (663)` with the list correctly auto-scrolled to its own
bottom, and the fixed input bar no longer overlaps the last card. This was
a pure CSS/layout change — no logic file was touched to fix it.

**Verified end-to-end after all 4 stages**, against the real
Docker-composed stack (rebuilt via `docker compose build frontend`), in
both themes: full Rule 7 flow (fault code → grid → click → real document
with byte-correct `intervento`), theme persists across reload, mic→
transcribe round-trip (via Playwright's fake-media-device flags), zero
console errors, and the IVECO regression above. No `ChatStore`/
`ChatApiService`/SSE-parsing/language-detection file was touched at any
point in this pass.

### 6.6 Frontend polish pass (borders, hover states, shadows, surface contrast) — complete

A second presentation-only pass over the redesigned frontend (section
6.5), same discipline - no `ChatStore`/`ChatApiService`/SSE-parsing/
`submit()`/`toggleRecording()` logic touched, CSS/templates/tokens only.

**A real, pre-existing bug found while auditing "harsh borders"**: every
bordered element (header, theme-toggle, confirmed-car badge, mic/send
buttons, car-card, repair-case-card, message bubble) used the CSS
shorthand `border: 1px solid;` / `border-bottom: 1px solid;` with no color
component. That shorthand resets the omitted color sub-property to its
*initial value* (`currentcolor`), and Angular's view-encapsulation
attribute selector gives the component-scoped rule higher specificity than
the plain `.border-border` utility class applied in the template - so
every one of these borders was silently rendering in the *text* color
(near-black in light mode, near-white in dark), never the intended soft
slate token at all. Confirmed via `getComputedStyle` before fixing
(`border-bottom-color: rgb(0,0,0)` on the header, matching its inherited
text color, not `--color-border`), not assumed from how it looked. Fixed
by splitting every such rule into `border-width`/`border-style` (or the
`-bottom-` equivalents) with no color sub-property, letting the
`border-border` utility's `border-color` apply uncontested. The exact same
bug, for `background` instead of `border-color`, also silently blocked the
`hover:bg-foreground/8` utility on `.theme-toggle`/`.reset-button` (both
had a redundant `background: transparent;` rule at the same tie-breaking
specificity) - fixed by deleting that redundant declaration; Tailwind's
own preflight already resets `<button>` backgrounds at a lower-specificity
element selector, so nothing relies on the component CSS for it.

**Hover states** added to all 4 interactive buttons via Tailwind's
`hover:` variant, no hardcoded one-off colors: `send-button` uses
`hover:bg-accent/90` (the exact pattern suggested), `mic-button`/
`theme-toggle`/`reset-button` use `hover:bg-foreground/8` (a foreground-
tinted overlay that automatically darkens on light backgrounds and
lightens on dark ones, since it's the *foreground* token doing the
tinting, not a separate per-theme value). **Tried `/5` first, rejected
after a real visual check**: technically applied (confirmed via
`getComputedStyle` background-color change), but imperceptible at normal
viewing scale in a screenshot - bumped to `/8`, re-screenshotted, clearly
visible in both themes at 1x scale, not just zoomed in.

**Shadows** added to exactly the 3 elements asked for - header, input-bar
pill, message bubbles - via a new `--shadow-color` custom property (not
inside `@theme`, since nothing generates a `shadow-color` utility from it;
it's consumed directly as `box-shadow: 0 1px 3px rgb(var(--shadow-color));`).
**Real, theme-aware values, not one flat shadow reused everywhere**: light
mode uses a navy-tinted `15 23 42 / 0.08` (subtle on white); dark mode
needed pure black at much higher alpha, `0 0 0 / 0.45`, since a
light-mode-appropriate low-alpha black shadow is nearly invisible against
an already-dark surface - verified by rendering both, not assumed. This
also fixed the *existing* input-bar shadow from Stage 3
(`rgba(0,0,0,0.12)`, hardcoded and applied identically in both themes,
never actually checked against the dark surface) onto the same
theme-aware token.

**Light-mode `surface` token darkened**: `#f8fafc` → `#eef2f6`.
`background` stays `#ffffff`. The original surface measured a 1.046:1
luminance-contrast ratio against background - close enough to invisible
that cards/panels blended into the page (confirmed by screenshot, not
just the small hex distance). `#eef2f6` measures 1.125:1 against
background while staying at 1.096:1 against `--color-border` (#e2e8f0) -
distinct enough to read as an elevated panel without crossing into the
same shade as its own border (which would have erased the border's edge
instead of complementing it - `#e2e8f0` itself, the border color, was
ruled out as a surface candidate for exactly this reason). `bg-surface` is
shared by every card/panel/input-bar in the app, so this one token change
propagated everywhere without touching individual component files.

**Verified after all changes** (rebuilt via `docker compose build
frontend`): IVECO 5-trim click regression re-run (14 cards, clicked
"40C-13" specifically, confirmation message still reads "...40C-13...");
theme toggle + `localStorage` persistence across reload; full Rule 7 flow
end-to-end in both light and dark (real document content present); zero
console/page errors throughout. Screenshots taken and visually inspected
for header, all 4 buttons (default + hover, both themes), message
bubbles, input bar, and car-selection cards - not just computed-style
assertions, per the same "visually confirmed, not just hex distance"
standard used for the Stage 2 accent-contrast check.

### 6.7 Frontend polish pass 2 (car-card distinctness, year-wrap, bubble gray) — complete

A third presentation-only pass, same discipline as 6.5/6.6 - no
`ChatStore`/`ChatApiService`/SSE-parsing/logic touched.

**Year-range wrapping fixed**: `.years` (e.g. "2011–2014") is a flex item
inside `.car-meta` with no `white-space` override, so at narrow grid-
column widths the row would squeeze it below its natural width and the
en-dash-joined digits would wrap mid-token onto two lines. Fixed with
`white-space: nowrap` on `.years`; `.car-meta` also gained `flex-wrap:
wrap` so the *other* meta items (engine code, power) can drop to a second
line instead, rather than the row overflowing the card. Verified at
1280px and at 380px (narrower than the grid's own `minmax(220px, 1fr)`
single-column breakpoint) in both themes - one line every time.

**Car-card vs. message-bubble background - two new tokens, not a reused
one**: `--color-surface` (still used by `repair-case-card`/the input-bar
pill/the header's confirmed-car badge - none of those were reported as a
problem) couldn't satisfy both asks at once: the message bubble needed to
read *lighter* than it ("too heavy" once every bubble inherited the
Stage-6.6 surface bump), while the car-card needed to read *more
pronounced* than the bubble it sits inside. Split into:
- `--color-bubble` (message bubbles only): light `#f6f8fa`, dark
  `#172033` - a real step lighter/closer-to-background than
  `--color-surface` in both themes (1.065:1 against it in light, 1.112:1
  in dark), confirmed by screenshot, not just the hex delta.
- `--color-card-surface` (car-selection cards only): light `#dde4ea`,
  dark `#283750` - distinct from both `--color-surface` (1.141:1 light,
  1.221:1 dark) and `--color-border` (1.041:1 light, 1.157:1 dark).
  **Real tradeoff surfaced, not silently absorbed**: `--color-muted` text
  sitting on top of the light card-surface lands at 3.707:1 contrast -
  below AA's 4.5 normal-text floor, but so was the pre-existing
  baseline (muted-on-surface was already 4.230:1 before this change). A
  bolder, more visually distinct candidate (`#d9e0e8`) was tried first
  and rejected for pushing that same text to 3.575:1 - picked the value
  that maximized visual separation without widening the existing gap
  further than necessary.

**Car-card shadow + hover**, reusing the existing `--shadow-color` token
(section 6.6) rather than inventing a new one: resting `0 1px 3px
rgb(var(--shadow-color))`, hover `0 4px 10px rgb(var(--shadow-color))`
(same color, bigger offset/blur - an "elevate" effect). Hover also keeps
the existing `hover:border-accent` (Stage 4) and adds `hover:bg-foreground/8`,
the same opacity-tint pattern used for every other bordered button in
Stage 6.6 - no new hover convention introduced.

**Verified after rebuild**: IVECO 5-trim click regression (14 cards,
clicked "40C-13" specifically, confirmation message still reads
"...40C-13..."); theme persistence across reload; full Rule 7 flow in
dark mode with real document content; zero console errors. Screenshots
of the car-grid and a message bubble taken and visually inspected in both
themes, plus a narrow-viewport (380px) screenshot confirming the year-wrap
fix holds at the grid's tightest column width.

### 6.8 FindCar "not found" specificity (suggested year range) — complete

A backend-only follow-up (Vehicle Service + Chat Service, no frontend
changes) distinguishing two cases that previously collapsed into one
generic "not found": brand+model genuinely don't exist at all, vs.
brand+model exist but not for the requested year.

**Vehicle Service** (`VehicleSearchService.SearchAsync`): when the main
query returns 0 rows *and* the request actually included a year filter
(`AnnoInizio`/`AnnoFine`), runs a second aggregate query - same
marca/modello/alimentazione/motorizzazione/codiceMotore/kw filters, no
year constraint - `SELECT MIN(anno_inizio_macchina), MAX(anno_fine_macchina),
COUNT(*)`. Zero rows there means the model doesn't exist at all (both
suggested fields stay null); 1+ rows means it exists for some range, which
gets returned as the new `VehicleResponse.SuggestedYearFrom`/
`SuggestedYearTo`. Deliberately gated on "a year filter was part of the
request" - a wrong brand/model with no year filter at all stays a plain
0-result response, no extra query run for no reason (verified: a
nonsense-model request with no year filter doesn't engage this path at
all).

**Chat Service** (`RepairOrchestrator.BuildChatResponse`): the message is
built deterministically from `VehicleResponse`'s real fields, never handed
to Gemini's formatting call as free text to paraphrase - same fidelity
principle as document content/car identity elsewhere in this build. The
formatting call still runs (it has no way to know which tool produced
`rawResult`), its `message` output is simply discarded for this specific
branch. Two templates, each in all 5 languages (it/en/fr/pt/es):
- No suggestion (`SuggestedYearFrom`/`To` both null): "We don't have a
  {marca} {modello} in our database."
- Suggestion present: "We don't have a {vehicleLabel} for {requestedYear},
  but we do have it from {suggestedFrom} to {suggestedTo}" - with
  `anno_fine_macchina`'s `9999` ("still in production, no end year yet")
  phrased as "to today" rather than literally "to 9999," per language.

**Real fact checked before testing, not assumed**: queried `gup_rows`
directly first - FIAT Ducato spans `2000`-`9999` overall, but the
*individual* row ranges have a genuine gap (`...2015–2019, 2021–9999...`),
so no row actually covers **2020** specifically - confirming "ho un ducato
del 2020" is a real, naturally-occurring not-found-with-suggestion case in
the live data, not a contrived test. FIAT Panda doesn't exist in `gup_rows`
at all, confirming the generic-case test is real too.

**Verified live** (direct Vehicle Service curl calls first to confirm the
SQL logic in isolation, then the full Chat Service pipeline through real
Gemini calls):
- `marca=FIAT&modello=Ducato&annoInizio=2020&annoFine=2020` →
  `{"count":0,"suggestedYearFrom":2000,"suggestedYearTo":9999}`.
- Same for Panda → both suggested fields `null`.
- A wrong-model query with no year filter at all → both fields `null`,
  confirming the fallback didn't engage needlessly.
- Full chat flow, Italian: "ho una panda del 2020" → "Non abbiamo un FIAT
  Panda a catalogo." (no fabricated range); "ho un ducato del 2020" → "Non
  abbiamo un Ducato per il 2020, ma è disponibile dal 2000 a oggi." (the
  real fact, correctly phrased).
- Same two cases re-verified in English and Spanish, confirming the
  template translates rather than only existing in Italian: English
  "We don't have a Ducato for 2020, but we do have it from 2000 to
  today."; Spanish "No tenemos un Ducato para 2020, pero lo tenemos de
  2000 a hoy."
- Confirmed the unrelated, pre-existing "found" paths (a normal FindCar
  with real results, a fault-code search) are unaffected by
  `BuildChatResponse`'s new signature/branching. **Correction, see 6.9**:
  this check only re-ran the *first* fault-code call (which returns a car
  list, never touching the new branch) - it did not re-run the full
  confirm-then-replay sequence, which is exactly where a real bug from
  this same change turned up next.

### 6.9 Two real bugs found from a live screenshot, both fixed

A live conversation (screenshot from the mechanic-facing UI) surfaced two
separate, real bugs - one frontend, one a direct consequence of 6.8's own
change that the verification in 6.8 didn't actually exercise.

**Bug 1 - wrong-language reply (frontend).** A fully Italian conversation
ended with a French sentence: "Nous n'avons pas de FIAT Panda dans notre
base de données." Root cause: `language-detector.ts`'s `detectLanguage()`
took tinyld's top-ranked language with no confidence floor at all. Tested
directly: `"0.9 TwinAir"` (a mechanic typing an engine label, not prose)
scored `fr` at `0.143` - the *same magnitude* a genuine Italian sentence
scores for `it` (`"ho una panda del 2020"` → `it` at `0.140`). The score
alone never distinguished a confident detection from a coin-flip on short
input; only length did. That one short reply flipped the *session's*
"last detected language," and every message afterward - including
section 6.8's new hardcoded template - silently inherited French. This
had been invisible before 6.8 because the old Gemini-generated free-text
reply tended to stay in whatever language the conversation was actually
in regardless of the language code it was told; a hardcoded per-language
switch has no such smoothing. **Fix**: `MIN_WORDS_FOR_DETECTION = 3` -
below this word count, `detectLanguage()` skips tinyld entirely and keeps
the session's prior language. Verified by replaying the exact failing
conversation end to end - stayed Italian throughout afterward.

**Bug 2 - a real regression from 6.8's own `BuildChatResponse` change
(backend).** The same screenshot showed the bot replying "we don't have
this vehicle" immediately after the mechanic had already confirmed a real
car - for a fault code that has a real, working document. Root cause:
Search Service's `/api/search/fault-code|symptom|system` responses always
include an empty `"cars": []` placeholder field *alongside* the real
`"documents"` array (confirmed via direct curl). Section 6.8's new
`BuildChatResponse` branch checked only for the *presence* of a `cars`
property, regardless of which tool was actually called - so it fired on
every successful fault-code/symptom/system search too, discarding the
real found document and replacing it with a fabricated "vehicle not
found" message. 100% reproducible (5/5, then 5/5 again), and - importantly
- reproduced identically even with the routing prompt fully reverted to
its original text, which is what proved this was a deterministic backend
bug, not an LLM routing problem (a different theory tested and ruled out
first - see below). **Fix**: gate the not-found branch on
`call.Name == "FindCar"` in addition to the shape check - shape alone
isn't enough to tell "Vehicle Service's genuinely empty result" apart
from "Search Service's harmless always-present empty field." The
pre-existing `cars.length > 0` branch above it (car-selection lists, used
by both FindCar and Search Service) is untouched and still ungated, since
it was never the problem.

**A separate, real fix attempted and reverted along the way.** Before
finding bug 2, a second hypothesis was tested: that Gemini was misrouting
to FindCar again after car confirmation because the routing prompt didn't
emphasize calling FindCar immediately enough on its first use. Two
prompt-wording attempts were tried (and both made the Rule 7 replay
mechanism fail 5/5 in testing) before reverting the *FindCar/Rule-7*
bullet to its exact original text and isolating the real cause to bug 2
above. The original "don't ask for fuel/engine/year before the first
FindCar call" UX goal was kept, but as a fully separate, narrowly-scoped
bullet ("The FIRST time you call FindCar in a session...") that doesn't
touch the existing, already-reliable Rule 7 wording at all - re-verified
not to affect Rule 7's reliability once bug 2 was actually fixed.

**Verified after both fixes, together**: the original failing
conversation (fault code → car selection → confirm → fault code again)
now correctly shows the real document, 5/5 trials; the 6.8 not-found
messages (Panda/Ducato) still correctly produce their respective
messages; a normal FindCar-with-results query is unaffected; the IVECO
5-trim click regression and the full Playwright fault-code-to-document UI
flow both still pass; zero console errors.

### 6.10 Removed two redundant car-confirmation messages

The same live screenshot also showed the confirmation step producing
*three* messages where one would do: the frontend's own synthetic
"Confermo il veicolo: FIAT Ducato 2.2 Multijet 16v" user bubble, a
hardcoded "Veicolo confermato: FIAT Ducato 2.2 Multijet 16v (46356294).
Descrivi il problema..." bot reply, and then Gemini's own natural
acknowledgment ("Ho memorizzato la tua selezione: ... Come posso
aiutarti?") from the routing call that runs right after - which already
covers the same ground on its own. Confirmed both were exactly what they
looked like - hardcoded, not generated - before removing either:
- **Frontend** (`ChatStore.confirmCar`): the synthetic confirmation text
  is still sent to the backend unchanged (Gemini still needs it in
  `History` for Rule 7's replay-the-pending-search mechanism), it's just
  no longer pushed into the visible `messages` signal. Added a
  `{ showUserMessage: false }` option to the shared private `send()`
  rather than a second near-duplicate method.
- **Chat Service** (`RepairOrchestrator.HandleMessageAsync`): removed the
  `yield return` of the hardcoded `BuildConfirmationMessage` text (and the
  now-unused method itself) from the `confirmingNewCar` branch.
  `ConfirmCarAsync`'s call and the synthetic fact added to `session.History`
  right after are both unchanged - only the extra static reply yielded
  directly to the frontend was removed; nothing Gemini-facing changed.

**Verified live**: confirming a car now shows exactly one assistant
message - Gemini's own natural reply (e.g. "Ok, veicolo confermato: FIAT
Ducato 2.2 Multijet 16v, codice motore 46356294. Come posso aiutarti?"),
generated from the same synthetic fact as before. Re-ran both standing
regressions to confirm this didn't disturb Rule 7 or car identity: the
IVECO 5-trim click test (still correctly says "40C-13" in Gemini's own
acknowledgment, not just the old hardcoded template) and the
fault-code-alone → confirm → document flow (still shows the real
`GUP97380` document). Zero console errors; a full-page screenshot of the
fault-code flow confirmed no visual gap where the two removed messages
used to sit.

---

### 6.11 Language-detection bug, round 2: a real Italian sentence
misdetected as Portuguese, then a regression found in testing the fix

A live screenshot showed an all-Italian conversation where, after the
mechanic typed "Accensione spia avaria motore" (a genuine 4-word symptom
report), the *entire returned repair document* rendered in Portuguese -
not just UI chrome. `language` flows straight into Search Service's
`lang` query param, which picks which language's row to return from the
per-language `documents` table (see 6.3/6.4), so a detection error here
is a real wrong-document bug, not a cosmetic one.

This is a second instance of the class of bug fixed in 6.9
(`language-detector.ts`'s word-count floor), but a different failure
mode. The 6.9 fix only ruled out inputs *too short* to carry signal
("0.9 TwinAir", 2 words). This new input clears that floor at 4 words and
is genuine prose - the bug is that `tinyld`'s own accuracy score put the
wrong language narrowly ahead of the right one:

```
"Accensione spia avaria motore" -> pt: 0.0385, it: 0.0337  (ratio 1.14, WRONG)
```

Gathered `detectAll` scores for a dozen more real phrases (known-good
detections from this session, plus new realistic symptom sentences) to
check whether the *gap* between the top two candidates - not the raw
score - separates a confident detection from a coin-flip. It does: every
correct detection tested had a top1/top2 ratio of 1.35 or higher; the one
wrong detection sat at 1.14. Added `MIN_CONFIDENCE_RATIO = 1.3`: if the
top candidate isn't at least 30% ahead of the runner-up, treat it as
unreliable and keep the session's already-established language instead
of trusting the coin-flip - same "don't flap on weak signal" principle
the word-count floor already uses.

**Verified this against the originally-reported phrase plus all 15
previously-tested phrases from this session (12 new realistic Italian/
English/French/Portuguese/Spanish samples, plus the 3 known-good ones
from 6.9)** - all 15 resolved correctly, including the one that was
previously broken.

**Regression found in testing, before this reached the user**: ran the
fix against the original 6.9 bug case again, but phrased the way a
mechanic would actually answer a follow-up ("si, 0.9 TwinAir" instead of
the bare engine label alone) - this now misdetected as French
(`fr: 0.175` vs `it: 0.112`, ratio 1.56, comfortably clears
`MIN_CONFIDENCE_RATIO`). Root cause: the naive word count treated "si,"
and "0.9" and "TwinAir" as three words, clearing `MIN_WORDS_FOR_DETECTION
= 3`, but only "si" is an actual word - "0.9" is a number and "TwinAir"
is an engine-trim label, and together they were enough non-prose content
to make `tinyld` confidently (by its own ratio) call the whole message
French.

Fixed by changing what counts as a "word" for the floor: a token only
counts if, after stripping punctuation, it's letters-only (no digits) and
at least 2 characters. Numbers and alphanumeric codes no longer pad the
count. Re-verified all 15 prior phrases plus both phrasings of the
TwinAir case (15 + 2 = 17 cases) - all correct, including the regression
just found.

**Verified live** (Playwright against the real Docker stack, request
payloads inspected directly, not just rendered text):
- The originally-reported phrase, full round trip: confirmed a FIAT
  Ducato, sent "Accensione spia avaria motore", confirmed the POST body
  carried `language: "it"` and the returned document rendered fully in
  Italian (`Impianto`/`Dispositivo`/`Anomalia`/`Causa`/`Intervento`/`Nota`).
- Rule 7 flow (symptom before car confirmation, then confirm) still
  replays correctly - unaffected, different code path.
- Redundant-confirmation-message removal (6.10) still holds - one
  assistant bubble per turn, no duplicates.
- The regression case ("si, 0.9 TwinAir") now correctly sends
  `language: "it"`.
- Zero console errors throughout.

| Decision | Why |
|---|---|
| Confidence-gap check (`top1.accuracy / top2.accuracy >= 1.3`) added alongside the word-count floor | Raw accuracy score doesn't separate confident vs. coin-flip detections; the *gap* between top two candidates does, confirmed against 15 real phrases |
| Word-count floor changed from raw whitespace split to letters-only, no-digit, ≥2-char tokens | Numbers and alphanumeric codes/trim labels were padding the word count, letting non-prose-dominated messages slip past the floor (found via testing, not the original report) |
| Did not raise `MIN_WORDS_FOR_DETECTION` itself | Would have pushed legitimate 4-word sentences like the reported case to always fall back on word count alone, masking rather than fixing the underlying scoring/counting issues |

---

### 6.12 Repair-case card restructured to match the production site's
field grouping

User reference: a screenshot of the real `app.semarepair.it` repair-guide
page, which groups document fields into two labeled sections -
"Identificazione del sistema / guasto" (Impianto, Dispositivo, Anomalia,
DTC fault codes, Causa) and "Procedura di riparazione" (Intervento,
Procedura, Nota) - instead of the flat list this card previously
rendered. Explicit instruction: match the *data structure* (field
grouping/order), not the visual template (colors/fonts/chrome).

Checked the full pipeline before touching anything: every field the
reference page shows already existed end-to-end with no gaps -
`documents` table (`impianto`/`dispositivo`/`anomalia`/`causa`/
`intervento`/`procedura`/`nota`/`reliability`) → `DocumentContentService`
→ `SearchResponse.DocumentResult` → `RepairOrchestrator.ParseCaseSummary`
→ `ChatResponse.CaseSummary` → frontend `CaseSummary` model. This was a
**frontend-only template restructuring** - `repair-case-card.component.ts`
- no backend/schema changes.

Changes:
- Added two section subheadings (`.section-heading`, small/uppercase/
  muted, matching the existing label styling system) splitting the
  card into the same two groups as the reference page.
- Moved the DTC-code badges from a separate block at the very bottom up
  into the identification section, between Anomalia and Causa, labeled
  "Errori rilevati dall'autodiagnosi:" (the reference page's own label)
  instead of an unlabeled badge row - new `.dtc-row` flex wrapper, badges
  unchanged (still `app-dtc-badge`, not converted to plain text).
- Reliability stays as the existing star-rating widget in the header,
  not moved into its own "Grado di attendibilità" block - same
  underlying `reliability` field, just already presented as stars rather
  than a written grade legend; not a data-structure change.

**Caught before implementing**: planned to translate the two new section
headings per session language (`caseSummary.language`), consistent with
how the rest of the app's chrome is localized elsewhere. Checked the
existing card first and found all 7 of its current field labels
(Impianto/Dispositivo/Anomalia/Causa/Intervento/Procedura/Nota) are
hardcoded Italian regardless of `caseSummary.language` - an existing,
established convention for this specific component (these are themselves
short structural field tags, not assistant-generated chrome). Translating
only the 2 new headings would have made the card inconsistent with
itself. Reverted to hardcoded Italian for the new headings too, matching
the other 7 labels already there, rather than introducing partial
translation or scope-creeping into translating all 9.

**Caught before shipping**: first draft of `.section-heading` CSS used
`border-top-color: rgb(var(--color-border) / 0.5)`, copying the alpha
pattern from `--shadow-color`. `--color-border` is a plain hex value
(`#e2e8f0` / `#334155`), not a space-separated RGB triplet like
`--shadow-color` is - only `--shadow-color` was deliberately defined in
that special format for `rgb()/alpha` use (see 6.6). `rgb(#e2e8f0 / 0.5)`
is invalid CSS. Fixed by using `var(--color-border)` directly, full
opacity, no alpha wrapper.

**Verified live** (Playwright, real Docker stack, real document - GUP97393,
a Debimetro fault with DTC P0101 attached): confirmed the rendered card
text groups in the exact order - Impianto → Dispositivo → Anomalia →
"Errori rilevati dall'autodiagnosi: P0101" → Causa → "PROCEDURA DI
RIPARAZIONE" → Intervento → Nota. Screenshotted both light and dark
theme - section dividers, labels, and the relocated DTC badge all render
correctly in both, no contrast issues. Zero console errors.

| Decision | Why |
|---|---|
| Frontend-only change, no backend/schema work | Every needed field (including `procedura` and `reliability`, already in the `documents` table) was already flowing end-to-end unused by the old flat template |
| New section headings hardcoded Italian, not translated | Matches this component's own existing, established convention - its other 7 field labels are already hardcoded Italian regardless of session language; translating only the 2 new ones would be inconsistent |
| DTC badges relocated + labeled, not converted to plain text | User said match data structure, not template - badges are an existing deliberate design choice, not part of "structure" |
| `var(--color-border)` instead of `rgb(var(--color-border) / 0.5)` | `--color-border` is a plain hex value; only `--shadow-color` is defined as a raw RGB triplet for alpha-aware `rgb()` use |

---

### 6.13 Repair-case card: Titolo + all 3 real Capitolo/Corpo, including
the previously-discarded reliability legend

User instruction: every document must show its Titolo (bold, centered,
top of card) plus all 3 of the source resx's `<XCAPITOLO>` chapters and
the `Corpo` (body) of each.

Checked the real source data first (`Data/resx_samples/*.resx`) before
changing anything:
- Every document has exactly 3 chapters, positionally fixed
  (`resx_parser.py` already documents this): Ordine=1 "Grado di
  attendibilità (*)", Ordine=3 "Identificazione del sistema / guasto",
  Ordine=4 "Procedura di riparazione".
- Chapters 2 and 3's Capitolo title text matches **verbatim** what 6.12
  already added as hardcoded section headings, and their Corpo content is
  already fully shown there via the parsed Impianto/Dispositivo/Anomalia/
  Causa/Intervento/Procedura/Nota fields - nothing missing for those two.
- Chapter 1 ("Grado di attendibilità") is the real gap: `resx_parser.py`
  only ever extracts the star count from its Capitolo title
  (`_parse_reliability`) into `reliability` - the Corpo itself (the
  legend explaining what the asterisks mean) is parsed by nothing and
  was never stored anywhere, in any language.
- Checked whether that legend Corpo is real per-document data or
  universal boilerplate before deciding where it should live: compared
  it across 3+ different IT documents (`199310632`, `199310628`,
  `199309631`) - byte-for-byte identical. Confirmed the EN/FR/ES/PT resx
  samples also each have their own fixed, internally-consistent
  translation of the same legend. It's universal system text, not
  per-document content.

Changes:
- **Titolo**: was already captured by `resx_parser.py` → `documents`
  table → `DocumentContentService` → `SearchResponse.DocumentResult`, but
  silently dropped at the next hop - `ChatResponse.CaseSummary` never had
  a `Titolo` field and `RepairOrchestrator.ParseCaseSummary` never mapped
  it. Added `Titolo` to `CaseSummary` (`ChatResponse.cs`), mapped it in
  `ParseCaseSummary`, added `titolo: string` to the frontend `CaseSummary`
  interface, and rendered it bold + `text-align: center` at the very top
  of the card, above the sigla/star-rating header row. No schema/
  ingestion change needed - the data already existed, it was a pure
  plumbing gap.
- **Chapter 1 legend**: added as a new first section, heading "Grado di
  attendibilità (*)" (hardcoded Italian, matching 6.12's own established
  rule for headings/field labels in this card) followed by the verbatim
  legend text, **translated per `caseSummary.language`** - pulled
  directly from the real resx samples in all 5 languages (it/en/fr/es/
  pt), not invented/approximated. No schema or ingestion change: since
  this text never varies per-document, it's hardcoded as a small
  language-keyed lookup directly in `repair-case-card.component.ts`,
  the same category as `RepairOrchestrator`'s other hardcoded
  per-language static text (Rule 9 clarification questions, FindCar
  not-found messages) - real system text that's identical for every
  document, not document content subject to the fidelity-from-a-
  real-query rule.
- Translating the legend body while keeping its heading hardcoded
  Italian is a direct extension of 6.12's existing pattern, not a new
  inconsistency: document *content* (Anomalia/Causa/etc. text) already
  varies by language via `documents` table row selection; only the
  surrounding *labels* stay Italian. The legend's explanatory prose is
  content (it must be readable by a non-Italian-speaking mechanic); its
  heading is a label.

**Verified live** (Playwright, real Docker stack, real document -
GUP97393): card now renders, in order - Titolo (bold, centered) → sigla +
star rating → "GRADO DI ATTENDIBILITÀ (*)" + full 3-level legend text →
"IDENTIFICAZIONE DEL SISTEMA / GUASTO" (Impianto/Dispositivo/Anomalia/
DTC P0101/Causa) → "PROCEDURA DI RIPARAZIONE" (Intervento/Nota).
Screenshotted both light and dark theme - correct contrast and layout in
both. Zero console errors. Rebuilt and verified both `chat-service`
(model/mapping change) and `frontend` (template/model change) together.

| Decision | Why |
|---|---|
| `Titolo` threaded through `CaseSummary`/`ParseCaseSummary`, no schema change | Data already existed all the way to `SearchResponse`; it was dropped one hop later, a pure backend-model gap, not missing data |
| Chapter 1 legend hardcoded per-language in the frontend, not re-ingested into `documents` | Verified identical across multiple documents per language - universal system text, not per-document data; re-ingesting it would treat boilerplate as if it were a real per-document fact |
| Legend heading stays Italian, legend body translates | Extends 6.12's existing rule consistently: labels stay Italian project-wide in this card, content (which this legend's body actually is) already varies by language elsewhere |

---

### 6.14 DTC fault-code descriptions were parsed during ingestion, then
silently discarded - now stored and shown

User-reported: a screenshot showing a document with 3 fault codes
(P0504/C1215/B1024) rendered as bare code badges, no description -
despite the source resx text clearly carrying a description per code
(e.g. "P0504 (Relazione interruttore freno 1-2 - Rapporto errato)").

Traced the full pipeline before touching anything: `resx_parser.py`
already has `_parse_fault_code_descriptions()` and returns a
`fault_code_descriptions` dict from every parsed record - but
`seeder.py`'s `DOCUMENT_COLUMNS` never included it, and
`graph_builder.py`'s `CONTAINS_FAULT` edges only ever carried
`(document, code)`, no description. The dict was computed and then never
read by anything - a pure ingestion gap, same shape as 6.13's Titolo
bug, but one step earlier in the pipeline.

Checked whether a description is safe to treat as one fact per `(code,
language)` globally, or whether it must stay scoped per-document, before
picking a storage shape: grepped multiple documents for the same code
(P0148) across different resx files and found two different phrasings
("Porta carburante" vs "Portata carburante" - a likely source typo in
one document). Confirmed descriptions are **not** uniformly clean across
documents, so a global `fault_codes(code, language)` table would have
forced silently picking a winner and losing the other document's actual
text - exactly the kind of approximation this build's fidelity principle
rules out. Storing it directly on the existing `CONTAINS_FAULT` edge
(already one row per `(document, code, language)`) avoids that problem
entirely - each document keeps its own real text, no merging.

Changes (schema → ingestion → both backend services → frontend, in order):
- `schema.sql`: added `description TEXT` to `graph_edges`, via
  `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` (the table already existed
  with data in the running database - a `CREATE TABLE IF NOT EXISTS`
  edit alone wouldn't have applied to it).
- `graph_builder.py`: every edge tuple grew a 7th `description` element
  (`None` for all 6 other edge types); `CONTAINS_FAULT` edges now carry
  `descriptions.get(code)` from the already-parsed dict.
- `seeder.py`: `INSERT INTO graph_edges` updated to include the new
  column.
- **Re-seeded real data**: `TRUNCATE graph_edges` (seeder.py only
  rebuilds a table when it's empty - editing the schema/builder alone
  doesn't retroactively backfill existing rows) and re-ran
  `ingestion-resx` - rebuilt all 3443 graph edges with descriptions.
  Verified directly via `psql` against the user's own reported codes
  before touching the frontend: `P0504/C1215/B1024` on document
  `199309631` came back with the exact text from the resx file.
- `SearchResponse.cs`/`DocumentContentService.cs`: added a
  `FaultCodeInfo { Code, Description }` class, changed
  `DocumentResult.DtcCodes` from `List<string>` to
  `List<FaultCodeInfo>`, updated the `CONTAINS_FAULT` query to select
  `description` too.
- `ChatResponse.cs`/`RepairOrchestrator.cs`: mirrored the same
  `FaultCodeInfo` class and `CaseSummary.DtcCodes` type change;
  `ParseCaseSummary` now parses `{code, description}` objects instead of
  bare strings, spliced directly from Search Service's JSON (same
  fidelity reasoning as the rest of `CaseSummary` - never from Gemini).
- Frontend `chat.models.ts`: added `FaultCodeInfo` interface, changed
  `dtcCodes: string[]` to `dtcCodes: FaultCodeInfo[]`.
- `repair-case-card.component.ts`: each DTC code now renders on its own
  line - the existing `app-dtc-badge` (unchanged) followed by its
  description text, instead of a flex-wrapped row of bare badges (which
  had no room for description text anyway). `app-dtc-badge` itself
  wasn't touched - confirmed it's used nowhere else in the app first.

**Verified live** (Playwright, real Docker stack, multiple real
documents): confirmed a FIAT Ducato, triggered a document with 5 DTC
codes (`GUP97444` - P0148/P1612/P0380/P0382/P0500), each rendering with
its own correct description on its own line. Also confirmed the earlier
data-quality finding is preserved correctly, not silently fixed/merged:
GUP97402's P0148 still reads "Portata carburante" while GUP97444's P0148
still reads "Porta carburante" - two different documents, two different
real texts, exactly as ingested. Screenshotted in both light and dark
theme - correct layout and contrast in both. Re-ran the Rule 7 regression
(confirms a document with *zero* DTC codes still renders correctly,
i.e. the `@if (dtcCodes.length > 0)` guard wasn't broken by the type
change). Zero console errors throughout. Checked `GraphSearchService.cs`
for any `graph_edges` query that could break from the new column - all
of its queries use explicit column lists, never `SELECT *`, so nothing
else was affected.

| Decision | Why |
|---|---|
| `description` added to the existing `CONTAINS_FAULT` edge row, not a new global `fault_codes(code, language)` table | Verified the same code can have slightly different phrasing across documents (real data, not a bug) - a global table would force merging/picking a winner, losing real per-document text |
| Re-seeded by truncating just `graph_edges` and re-running `ingestion-resx`, not a full `documents`/embeddings rebuild | `seeder.py` skips any table that isn't empty; `documents` and `document_embeddings` already had everything needed and didn't need to be touched (avoids re-spending Gemini API calls) |
| `app-dtc-badge` component left unchanged | It only renders the bare code; used nowhere else in the app, so the description was added as a sibling element in the card's template rather than changing the badge's own API |

---

### 6.15 Confirmed-car header badge: centered, expanded to all 9
VehicleResult/CarOption fields - plus a second pipeline gap found
alongside the one asked about

User instruction: move the confirmed-car badge from the right of the
header to horizontal center, and expand it from brand+engine code to all
9 fields, two lines (marca+modello bold; motorizzazione/year-range/
alimentazione/kw+cavalli/codiceMotore smaller/muted). Presentation-only;
explicitly asked to verify the pipeline first rather than have anything
silently fabricated or dropped.

**Checked before changing any layout, per instruction**:
- `ChatStore.confirmedCar` signal already retains the *full* clicked
  `CarOption` object (`carBeingConfirmed`, unmodified) - not narrowed at
  confirmation time. No fix needed there.
- `alimentazione` (the field asked about): present on `VehicleResult`
  (Vehicle Service), but **missing from `ChatService.Models.CarOption`**
  - `RepairOrchestrator.ParseCarOption` stopped at `Cavalli`, never
    mapped it. Confirmed gap, exactly the hop asked about.
- **Found a second, deeper gap while tracing `ParseCarOption`'s actual
  callers, flagged before fixing rather than silently expanding scope**:
  `ParseCarOption` is shared by *two* different sources - Vehicle
  Service's `FindCar` results, and Search Service's own car-selection
  responses (Rule 1/2 - shown whenever a fault-code/symptom/system
  search has no confirmed car yet, e.g. the IVECO Daily III 5-trim
  scenario from earlier this session). Search Service's `CarSummary`
  class **and the underlying SQL** in `GraphSearchService.cs`
  (`GetCarsForDocumentAsync`/`GetCarSummariesAsync`) never selected
  `kw_macchina`/`cavalli_macchina`/`alimentazione_macchina` from
  `gup_rows` at all - not a class-mapping omission, those 3 columns were
  never queried. Fixing only the Vehicle Service hop would have left the
  new 9-field badge silently blank for 3 fields whenever a car was
  confirmed via this second, equally-common path. User confirmed: fix
  both pipelines now.

**Backend changes** (both pipelines, so the badge is complete regardless
of which flow confirmed the car):
- `services/search/Models/SearchResponse.cs`: added
  `Alimentazione`/`Kw`/`Cavalli` to `CarSummary`.
- `services/search/Services/GraphSearchService.cs`: both
  `GetCarsForDocumentAsync` and `GetCarSummariesAsync` now select
  `alimentazione_macchina`/`kw_macchina`/`cavalli_macchina` and populate
  the 3 new fields.
- `services/chat/Models/ChatResponse.cs`: added `Alimentazione` to
  `CarOption`.
- `services/chat/Services/RepairOrchestrator.cs`: `ParseCarOption` now
  maps `alimentazione` from the raw JSON, same fidelity reasoning as the
  rest of `CarOption` (spliced from Search/Vehicle Service's response,
  never from Gemini).
- Frontend `chat.models.ts`: added `alimentazione?: string | null` to
  `CarOption`.

**Layout** (`app.component.html`/`.css`/`.ts`, no `ChatStore`/
`ChatApiService`/`RepairOrchestrator` logic touched):
- `.app-header` changed from `flex` + `justify-content: space-between`
  to a 3-column grid (`1fr auto 1fr`) - a flex layout with the badge as
  a middle child centers it relative to the *leftover* space next to the
  logo and actions, not the header's true center; the grid's middle
  column doesn't have that problem.
- Reset button moved out of the badge into `.header-actions`, staying on
  the right next to the theme toggle, exactly as asked - only the
  car-info badge itself moved to center. Still conditional on
  `confirmedCar()`, still calls `chat.reset()`, unchanged otherwise.
- `.confirmed-car` restructured to a centered 2-line stack: line 1
  `marca + modello` (bold, 13px); line 2 a single joined string from a
  new `carDetails(car)` helper on `AppComponent` - motorizzazione,
  year-range, alimentazione, "kw kW / cavalli CV" (reusing `car-card`'s
  exact power-string format for consistency), codiceMotore, joined with
  " · ".
- `annoFine === 9999` renders as "oggi" in this new badge specifically
  (matching `RepairOrchestrator.BuildVehicleNotFoundMessage`'s existing
  "dal X a oggi" convention) - deliberately *not* applied to
  `car-card.component.ts`, which still shows the literal "2021–9999" for
  the selection-list cards; out of scope, presentation-only change
  confined to the new badge as the instruction described.

**Verified live** (Playwright, real Docker stack, both pipelines, both
themes): FindCar path (`FIAT Ducato 2.2 Multijet 16v`, confirmed via the
single-result auto-confirm) showed "2.2 Multijet 16v · 2021–oggi ·
Diesel · 103 kW / 140 CV · 46356294" centered, Reset+theme-toggle still
on the right. Search Service car-selection path (`P0504`'s 7-car list,
no car confirmed beforehand) showed the same complete fields after
clicking a card, alongside the DTC-description fix from 6.14 in the same
document. Re-ran the standing IVECO 5-trim regression (`motore
8140.43S`, 14 cards across multiple brands): clicking the specific
"IVECO Daily III 50C-13 8v" card resolved to exactly that trim, not an
arbitrary one sharing the engine code, badge showing all 9 fields
including `Diesel`. Zero console errors across all three scenarios.

| Decision | Why |
|---|---|
| Fixed Search Service's `CarSummary`/SQL too, not just Vehicle Service's `CarOption` | Found mid-task that `ParseCarOption` is shared by both pipelines; fixing only one would leave the new badge silently incomplete depending on which flow confirmed the car - flagged and confirmed with the user before expanding scope |
| 3-column CSS grid instead of flex for `.app-header` | Flex centers a middle child relative to leftover space beside its siblings, not the container's true center when the siblings have different widths (logo vs. theme-toggle) |
| "oggi" conversion added only to the new badge, not `car-card.component.ts` | Explicitly scoped by the instruction to this one component; `car-card`'s existing "2021–9999" display is unrelated, untouched |

---

### 6.16 Confirmed-car badge: text "Reset" button replaced with a small
circular close ("×") badge on the card's own corner

Follow-up to 6.15's centered badge: removed the `.reset-button` text
button from `.header-actions` entirely and replaced it with a small
circular button pinned to the confirmed-car badge's own top-right
corner - the classic dismissible-chip pattern. Presentation-only;
`chat.reset()` is still the only thing it calls, unchanged.

- `.confirmed-car` got `position: relative` so the new `.close-badge`
  button can be positioned `absolute` against it (`top:-7px; right:-7px`,
  16×16px, `border-radius: 50%`).
- `.close-badge` uses `bg-background` (the page background) rather than
  `bg-surface` (the badge's own background) so the circle reads as a
  distinct, separate control sitting half on/off the badge's edge,
  rather than blending into it.
- `.header-actions` now holds only the theme toggle - the reset control
  moved entirely out of it, matching the original instruction ("delete
  reset button").

**Verified live** (Playwright, real Docker stack, both themes): confirmed
a FIAT Ducato, screenshotted the badge region directly (a plain
locator-scoped element screenshot clips anything positioned outside the
element's own box, so the absolutely-positioned circle didn't show up in
the first attempt - switched to a page-level screenshot with a padded
clip region around the badge's bounding box instead) - the × circle
renders correctly in the corner in both light and dark theme, with a
visible hover-darken effect. Clicked it and confirmed it correctly clears
both `confirmedCar` and the message list (same `chat.reset()` as the old
button). Zero console errors.

---

### 6.17 Gemini usage/cost dashboard - new cross-cutting feature, built in
6 sequential, individually-verified phases

Full design written up first as `docs/log-dashboard.md` (architecture +
build pipeline), then implemented exactly per that plan. Three real
Gemini call sites exist in this codebase, each behind a single
chokepoint method - `GeminiChatClient.GenerateAsync` (chat-service),
`QueryEmbedder.EmbedAsync` (search-service), `Embedder.embed` (Python
ingestion-resx) - so instrumentation meant touching 3 methods, not every
call site.

**Critical real-world fact found and designed around from the start**:
Gemini's `generateContent` always returns real `usageMetadata`;
`embedContent` (both embedding call sites) never does. Every embedding
usage row is therefore an *estimate* from input text length, never a
measured fact - `is_estimated` is a first-class column, never hidden,
shown explicitly in the dashboard's log table ("~stima" badge) rather
than presented identically to a measured chat-service row.

**A second, more subtle real bug found and fixed mid-build, before
shipping**: a direct raw API call proved `promptTokenCount +
candidatesTokenCount != totalTokenCount` for `gemini-2.5-flash` - the gap
is `thoughtsTokenCount` (2.5 Flash's hidden "thinking" tokens), which
Google bills at the output rate but reports as a separate field, not
folded into `candidatesTokenCount`. The original cost formula
(`prompt + candidates`) would have silently undercounted real cost on
every single chat-service call. Fixed by folding
`candidatesTokenCount + thoughtsTokenCount` into one `completion_tokens`
figure before computing cost, so `prompt_tokens + completion_tokens`
now exactly equals `total_tokens` and the cost figure reflects what
Google actually bills - verified against a real measured example
(7 + 1 + 19 = 27, confirmed via a direct curl to the real API bypassing
all of this app's own code).

**Schema** (`gemini_usage_log`, added to `services/ingestion-resx/schema.sql`,
the one place this codebase already treats as its schema source of
truth): `service_name`, `operation`, `model`, `prompt_tokens`/
`completion_tokens`/`total_tokens`, `is_estimated`, `cost_usd`,
`session_id` (reuses `ChatStore`'s existing per-tab session id - free
correlation from "this row" to "this mechanic's conversation"),
`ingestion_run_id` (FK to the pre-existing `ingestion_log.id` - one
ingestion run's total embedding cost is a join, not a new concept),
`raw_usage_json` (the verbatim source fact, kept even though it's
redundant with the parsed columns - lets cost be recomputed later if
pricing/estimation assumptions turn out wrong, without having
fabricated anything in the meantime).

**Pricing**: one shared `config/gemini-pricing.json`, mounted read-only
into ingestion-resx/search-service/chat-service, read once at startup
and cached - not a database table (a price change should be a reviewed
git diff, not a silent `UPDATE`), and not hardcoded per-service (a
shared file is the one way to avoid the C#/Python split drifting). The
file carries its own `_disclaimer`/`prices_verified_at` fields - prices
are a point-in-time fact from Google's pricing page, not something this
build can verify is still current on its own.

**Cost-calc/pricing-lookup class (`GeminiPricing.cs`) and the
non-blocking usage writer (`UsageLogger`/`UsageLogBackgroundService` -
a bounded `Channel<UsageRecord>` drained by a `BackgroundService` that
batches inserts) are duplicated verbatim in both chat-service and
search-service** - confirmed first, by checking the actual `.csproj`
files, that the three C# services have zero shared library and zero
project references between them already (`GeminiChatClient`/
`QueryEmbedder` already each hand-roll their own HTTP plumbing rather
than sharing a base class) - so this duplication is the existing
pattern repeating, not a new problem introduced by this feature.

**Why non-blocking matters here specifically**: chat-service had *no*
Postgres connection at all before this feature - giving it one for the
first time, on the same path that produces the live SSE-streamed chat
response, is the highest-risk change in this whole build. Verified by
stopping `our-postgres` mid-conversation and confirming a real chat
exchange (one that doesn't need any tool call, so no other,
unrelated Postgres dependency could mask the result) still answered
correctly in under a second, while the background drain logged a
caught warning ("Failed to write N usage log rows") instead of crashing
the service. Restarted Postgres afterward and confirmed new rows wrote
successfully again - clean recovery, no corrupted state.

**Reads are centralized in search-service** (`UsageQueryService` +
`UsageController`, `/api/usage/summary|timeseries|logs`), not split
across the three writers - one place for aggregation SQL
(parameterized, `GROUP BY`/`date_trunc`, same raw-SQL-no-ORM convention
as `GraphSearchService`). Every number returned was cross-checked
against an independent manual `psql` `SUM`/`COUNT`/`GROUP BY` query over
the same rows before the frontend was built against it - exact matches,
not "looks about right."

**Frontend**: this app had *no* Angular routing at all before this
feature (`app.config.ts` had no `provideRouter`, no `app.routes.ts`
existed) - the chat shell (header/message-list/input-bar, previously
all inline in `AppComponent`) was extracted into its own routed
`ChatPageComponent` at `''`, with the new dashboard at `'usage'`.
`AppComponent` now hosts a `<router-outlet>` plus the header chrome that
stays global across both routes (logo, theme toggle, confirmed-car
badge, and two new nav links). `ChatPageComponent`'s host needed
`display: contents` for exactly the same reason already documented in
`message-list.component.css` - without it, the extracted component
would introduce a new box into `.app-shell`'s flex column and break the
existing flex:1/fixed-height sizing of the message list and input bar.
New `usage.models.ts`/`usage-api.service.ts` mirror the backend
DTOs/`ChatApiService`'s plain-`fetch` convention exactly. The
"system load over time" chart is a hand-built SVG bar chart (no
charting library exists in this app, and didn't need to be introduced
for this) - one `<rect>` per time bucket, `<title>` for a plain hover
tooltip, scaled from real `costUsd` values, nothing else.

**Access guard (Phase 6, explicitly deferred to a deliberate decision
rather than skipped or auto-picked)**: this app has zero authentication
anywhere today, and the new dashboard exposes real spend data on the
same public nginx surface as the chat UI. Asked the user directly
rather than guessing; chose a shared-secret header. nginx's
`/api/usage` location now requires `X-Usage-Key` to match a secret
substituted in at container startup via the official nginx image's
`envsubst`-templating mechanism (`nginx.conf` mounted into
`/etc/nginx/templates/*.template` instead of `/conf.d` directly, with
`USAGE_DASHBOARD_KEY` added to `.env`/passed as an env var to the nginx
service) - the secret itself was never hand-edited into the committed
file. The frontend prompts for the key once (`window.prompt`, stored in
`localStorage`, never baked into the JS bundle at build time) only when
`/usage` is actually opened - the mechanic-facing chat UI never sees or
needs it. A wrong/stale key clears itself and re-prompts on the next
request rather than failing silently forever. Explicitly documented (in
both the architecture doc and the code) as a deterrent, not real
security - the frontend that sends this header is a public static
bundle, so the secret is visible to anyone who looks for it.

**Verified end-to-end, real data only, no mocks, at every phase boundary**:
real Gemini API calls (both `generateContent` and `embedContent`) logged
correctly with real, cross-checked costs; a real Postgres outage proven
not to break the live chat response; all three aggregation endpoints
matched independent `psql` aggregates exactly; the dashboard's rendered
KPI numbers matched those same psql-verified figures; the access guard
tested in all 4 states (no header/wrong header/correct header/unrelated
route unaffected) via direct curl, then the full prompt-store-retry flow
via a real browser. Zero console errors throughout.

| Decision | Why |
|---|---|
| Instrument the 3 existing chokepoints, not call sites | `RepairOrchestrator` alone calls `GenerateAsync` twice per turn plus once for transcription - one method change covers all of them |
| `is_estimated` as a first-class, always-visible column | `embedContent` never returns real usage - presenting an estimate identically to a measured fact would break this project's existing fidelity principle |
| Folded `thoughtsTokenCount` into `completion_tokens` | Found via a real direct API call that prompt+candidates didn't add up to the API's own total; the gap is billed output Google just reports separately - the original formula would have silently undercounted every chat-service row |
| `gemini-pricing.json` as a file, not a DB table | A price change should be a reviewed git diff, not a silent `UPDATE` with no audit trail |
| `GeminiPricing`/`UsageLogger` duplicated in chat-service and search-service | Confirmed first that the 3 C# services already share no library/project references at all - this is the existing pattern, not a new one |
| Bounded `Channel` + `BackgroundService`, not `Task.Run` fire-and-forget | Bounded (won't grow unbounded under load) and batches inserts instead of one round-trip per call; proven non-blocking by stopping Postgres mid-conversation and confirming chat still answered correctly |
| Reads centralized in search-service, writes stay per-service | One place for aggregation SQL instead of three; search-service already had the right shape (existing `OUR_DB` connection, existing raw-SQL aggregation style) |
| Hand-built SVG chart, no charting library | No charting library existed in this app and the internal dashboard's scale doesn't need one yet - richer interactivity is a real v2 problem, not a v1 prerequisite |
| Shared-secret header, asked the user rather than auto-picked | This app has zero auth anywhere today; exposing real spend data needed a deliberate decision, not a default - asked, then implemented exactly what was chosen |
| Secret substituted via nginx's own envsubst-templating, not hand-edited into the committed config | Keeps the real secret in `.env` only, consistent with how every other secret in this project (`GEMINI_API_KEY`, `POSTGRES_PASSWORD`) is already handled |

---

### 6.18 `modello` exact-match bug: a real IVECO Daily query returned
"not in catalog" despite the car existing - fixed

**What failed**: scenario 5 from `docs/chat-test-scenarios.md`
("ho un iveco daily con motore 8140.43S") returned "Non abbiamo un
IVECO Daily a catalogo" instead of the expected 5-trim disambiguation
list.

**Root cause**: `VehicleSearchService.SearchAsync` matched `modello`
with an exact `ILIKE` (no wildcards). `gup_rows` stores the full trim
name `Daily III`, not the bare `Daily` a mechanic naturally types.
Confirmed directly: `modello=Daily` → 0 rows, `modello=Daily III` → 5
rows, no `modello` filter at all (codiceMotore alone) → 14 rows across
4 brands sharing that engine code. So the car was always identifiable
by engine code alone - the exact-match `modello` filter was silently
discarding a real match.

**Collision check before fixing**: queried
`SELECT DISTINCT marca_macchina, modello_macchina FROM gup_rows` to
see whether wildcard-wrapping `modello` (the same treatment
`motorizzazione` already gets, two lines below it) could cause two
real, distinct models to collide. Found one case: FORD has both
`Focus` and `Focus C-Max`. Accepted this tradeoff for the same reason
already accepted for `motorizzazione`: `FindCar` always returns
matches as a selectable card list with each car's own model name
shown, so a mechanic seeing one extra card to ignore is strictly
better than a real car returning zero results.

**Fix**: wrapped `modello` in `%...%` wildcards in both `SearchAsync`
and `ComputeSuggestedYearRangeAsync` in
`services/vehicle/Services/VehicleSearchService.cs`.

**Verified, real data only, no mocks**:
- Direct vehicle-service curl: the original failing query now returns
  all 5 IVECO Daily III trims.
- Direct vehicle-service curl: FORD `modello=Focus` now returns both
  `Focus` and `Focus C-Max` rows (6 total) rather than silently
  failing - confirms the accepted tradeoff behaves as expected, not as
  a new bug.
- Real chat flow (actual Gemini `FindCar` routing via
  `/api/chat/stream`): the exact originally-failing message now
  returns the correct 5-trim `carMatches` list.
- Confirmation-by-specific-id still resolves correctly after the fix:
  confirming `idMacchina: IV5038` in the same session correctly
  acknowledged "IVECO Daily III 40C-13 8v con motore 8140.43S," not an
  arbitrary one of the 5 trims sharing that engine code.
- Regression spot-checks on unaffected scenarios: plain
  `marca=FIAT&modello=Ducato` still returns all 34 matching rows;
  `marca=FIAT&modello=Ducato&annoInizio=1995&annoFine=1995` (wrong
  year, real model) still returns `count: 0` with the correct
  suggested year range, unchanged from before this fix.

| Decision | Why |
|---|---|
| Wildcard-wrap `modello`, accept the Focus/Focus C-Max collision | Same reasoning already accepted for `motorizzazione`: `FindCar` always shows results as a selectable card list, so over-inclusion is recoverable and zero-results-for-a-real-car is not |

---

### 6.19 Symptom vector search had no relevance floor - a confirmed car
with zero matching documents still returned confident-looking but
unrelated results

**What failed**: continuing scenario 6 from `docs/chat-test-scenarios.md`
("freni che stridono quando frenano" after confirming an IVECO Daily III
45C-13) returned 4 documents about glow plugs, injectors, sporadic
stalling, and a fuel gauge fault - none related to brakes at all.

**Root cause**: `SearchController.SymptomWithCarAsync` ranks the
confirmed car's documents by vector distance and returns the closest
one(s) with no floor on how close "closest" actually has to be.
`TieThreshold` (0.02) only checks closeness *between* results, never
closeness to the actual query. Confirmed directly against the database:
the confirmed car (`IV5042`) has exactly 12 linked documents, all
Iniezione/Alimentazione motore/Elettronica faults - genuinely zero
brake documents - so the search still confidently returned the "least
bad" 4 as if they answered the question. The identical gap existed in
`SymptomSearchService.FindBestMatchesAsync` (Type 4, no car confirmed).

**Calibration, from real Gemini embeddings, not a guessed number**: embedded
the real failing query and a real working query (`"spia motore accesa scarse
prestazioni"`) via the production `gemini-embedding-001` endpoint, then ran
real cosine-distance queries against `document_embeddings`:
- True match (own document): **0.24**, with a 0.04 gap to the next candidate.
- Best topically-relevant real brake document in the whole dataset (a
  Freni/ABS document, not even an exact symptom match): **0.337**.
- `IV5042`'s genuinely unrelated documents: **0.398-0.449**.

This gave a clean, real-data-backed line: `MaxRelevantDistance = 0.35`
in `services/search/Controllers/SearchController.cs` - passes any
real topical match, excludes the unrelated cluster.

**Fix**: below `MaxRelevantDistance`, behavior is unchanged (existing
tie-grouping still applies). At or above it:
- Type 3 (confirmed car, has documents): return only the single
  nearest document, flagged `LowConfidenceMatch = true` with
  `LowConfidenceReason` set to that document's own real
  `Impianto`/`Dispositivo` (never invented text) - same transparency
  pattern as the existing `FoundViaSharedEngine`/`SharedEngineInfo`
  fields.
- Type 3 shared-engine fallback (confirmed car has *no* documents at
  all): treated as a real not-found rather than stacking a
  "low-confidence AND shared-engine" guess.
- Type 4 (no car confirmed yet): treated as a real not-found, since
  this path only ever exposes a car-selection list with no field to
  attach a disclaimer to - a misleadingly confident car list is worse
  than no list.
- `services/chat/Services/RepairOrchestrator.cs` (`BuildResultSummary`)
  now also extracts `lowConfidenceMatch`/`lowConfidenceReason` and
  passes them to the formatting call, exactly like the existing
  shared-engine fields.
- `services/chat/Services/SystemPromptBuilder.cs` (`BuildFormatting`)
  gained Rule 8b: when `lowConfidenceMatch` is true, state plainly that
  no document specifically matches the symptom, but the closest
  information available concerns `lowConfidenceReason` - never present
  it as a confirmed answer.

**Verified, real data and real chat flow, no mocks**:
- Direct search-service curl: the brake query now returns
  `lowConfidenceMatch: true`, `lowConfidenceReason: "Iniezione -
  Candeletta"`; the performance-light query on the same car still
  returns `lowConfidenceMatch: false` with its real match, unchanged.
- Real chat flow end-to-end (confirm `IV5042` via Gemini's actual
  routing, then ask about squealing brakes): Gemini's natural-language
  reply now says, verbatim, "Non è stato trovato un documento che
  corrisponda specificamente al sintomo descritto per questo veicolo,
  ma l'informazione più vicina disponibile riguarda Iniezione -
  Candeletta. Si prega di notare che questa non è una corrispondenza
  confermata per il sintomo." - honest, not fabricated, references the
  real document's real fields.
- Same session, real performance-light symptom: `message: null`, full
  document returned exactly as before - confirms the cutoff didn't
  regress the genuine-match path.
- No-car-confirmed symptom search (Type 4), both the brake query and
  the performance-light query against the whole dataset: both still
  return real `car_selection` lists (32 and 25 cars) - confirms the
  cutoff isn't so aggressive it breaks genuine whole-dataset matches.

| Decision | Why |
|---|---|
| Single fixed `MaxRelevantDistance`, not per-query/per-system tuning | The calibration data showed a clean, wide gap (0.337 best-real vs. 0.398 worst-unrelated) - no evidence yet that a single global cutoff is insufficient |
| Show the nearest document with a disclaimer (Type 3, has docs) instead of a hard not-found | The car genuinely has *some* documentation; hiding it entirely is less useful than honestly flagging it as unconfirmed - same reasoning as `FoundViaSharedEngine` |
| Hard not-found instead of a disclaimer (Type 3 shared-engine fallback, and Type 4) | Both paths have no per-document/per-car field to attach a disclaimer to - a misleadingly confident list is worse than nothing |
| `LowConfidenceReason` built from real `Impianto`/`Dispositivo`, not generated prose | Matches this project's fidelity principle: backend never hands Gemini anything it has to verify or might overstate |

---

### 6.20 Low-confidence match: ask before showing, not show-with-a-caveat

**Follow-up to 6.19**: showing the low-confidence document immediately
(with only the chat text disclaiming it) still wasn't right - the
document card itself rendered exactly like a confirmed match, caveat or
not. The mechanic should be asked first, and only see the document
after explicitly agreeing.

**Fix**: the document is now withheld on the turn that finds it. The
formatting call's message becomes a question (Rule 8b in
`SystemPromptBuilder.BuildFormatting`) instead of a statement, and
`RepairOrchestrator.BuildChatResponse` returns `Found = false`,
`Cases = []` for that turn - the frontend never receives the document at
all yet.

Showing it is then driven by Gemini's own routing decision, the same
way Rule 7's symptom-replay-after-car-confirmation already works
(deliberately *not* new sticky session state - see the existing
rationale in `Session.cs`): `SearchBySymptom` gained an optional
`confirmLowConfidenceMatch` boolean parameter
(`ToolDefinitions.cs`/`SystemPromptBuilder.BuildRouting`). Gemini sets
it to true only when its previous turn asked the low-confidence
question and the mechanic's new message is a clear affirmative reply,
re-issuing the exact same symptom text. Since the underlying search is
deterministic, this returns the identical document - `BuildChatResponse`
then exposes it in `Cases` and the formatting call switches to Rule 8c
(a short "this is the closest thing on record, not a confirmed match"
note instead of a question).

A follow-up wording tweak: Rule 8b initially produced a short, purely
interrogative message ("Vorresti vedere...?"). The actual desired
wording keeps the fuller disclosure statement first (no document
matches; the closest available concerns X; this isn't a confirmed
match) and appends the question at the end, matching the original
6.19 wording with a question added on - not a structural change, just
the prompt text.

**Verified, real chat flow, no mocks**:
- Confirmed IVECO Daily III 45C-13, asked about squealing brakes: got
  `found:false`, `cases:[]`, and the message "Non è stato trovato
  alcun documento che corrisponda specificamente al sintomo descritto
  per questo veicolo. L'informazione più vicina disponibile riguarda
  'Iniezione - Candeletta'. Questa non è una corrispondenza confermata,
  ma solo la cosa più vicina disponibile. Vuoi vederla comunque?" - no
  document leaked.
- Replied "si, mostramelo": got `found:true`, the real document in
  `cases`, and the short caveat message - confirms the affirmative path
  works.
- Separately, declined ("no, lascia stare") and asked about a real,
  unrelated symptom in the same message: got the correct real document
  (performance light/debimetro) - confirms declining doesn't leak the
  withheld brake-adjacent document, and a genuine match still flows
  through untouched.

| Decision | Why |
|---|---|
| Gemini-driven confirmation flag, not new session state | Mirrors Rule 7's existing design choice (`Session.cs`'s own comment): conversation history already gives Gemini everything it needs to decide, so a sticky "pending offer" field would be redundant state to keep in sync |
| Re-run the same deterministic search rather than caching the withheld document | Simpler - no new state to expire/invalidate - and the search is cheap and idempotent; the only thing that changes between the two turns is whether `Cases` gets populated |

---

### 6.21 Scenario 8 (symptom search, shared-engine fallback) is currently
dead code on real data - found while trying to test it, not a new bug

While trying to run `docs/chat-test-scenarios.md` scenario 8 for real
(confirm a car with zero documents of its own, ask a symptom matching a
shared-engine sibling's document), checked the actual precondition
directly against the database: every one of the 128 distinct
brand+engine combinations in `gup_rows` has at least one document of its
own. `SymptomWithCarAsync`'s shared-engine fallback only triggers when
`candidateDocs.Count == 0` (`SearchController.cs:121`) - a condition
that, on this dataset, can never be true.

This is a stricter trigger than the structurally equivalent fault-code/
system fallback (`TryFallbackAsync`, Rule 8), which falls back whenever
there's no document for that *specific* fault code/system - not "zero
documents at all." That one has already been verified live with real
data (a CITROEN Jumper 4HV falling back to a FIAT Ducato sibling, see the
Italian-naming-standardization log entry above). Type 3's stricter
condition predates this session's low-confidence-match work (section
6.19/6.20, which solves a related but distinct problem: a car *with*
documents, none of which are a good match for this symptom) and wasn't
touched by it.

**Not fixed** - this is a documentation finding, not a code change. Per
explicit user choice, left as-is: noted in both this file and
`docs/chat-test-scenarios.md` as currently unreachable with real local
sample data, structurally correct per the architecture doc, worth
revisiting either once real production data exists or if a future
session decides Type 3 should fall back on "no good match" the same way
Type 1/2 already does on "no exact match."

---

### 6.22 Two distinct faults in one message: the discarded symptom is no
longer silently lost

**What failed**: scenario 10 from `docs/chat-test-scenarios.md`
("il cambio slitta in terza marcia e sento anche rumore ai freni," no car
confirmed) returned a flat "Nessun risultato trovato" dead end.

**Root cause, confirmed against real data, not assumed**: the existing
symptom-cleaning rule (`SystemPromptBuilder.BuildRouting`'s "due sistemi
diversi" example) picks the more specific of two distinct faults and
discards the other entirely - it was never persisted anywhere, not even
in conversation history. Checked directly: the picked symptom ("cambio
slitta terza marcia") has no real match in the dataset at all (best
cosine distance 0.38, above the `MaxRelevantDistance` floor from section
6.19, and none of the candidates are even about a gearbox) - so
"not found" for that specific symptom is honest. But the discarded half
("rumore ai freni") does have real matches - searching the full original
sentence directly against search-service returned a real 32-car
selection. The mechanic's second, genuinely answerable symptom was thrown
away before ever being searched.

**Fix**: `SearchBySymptom` gained an optional `secondarySymptom`
parameter (`ToolDefinitions.cs`). The routing prompt's existing
two-distinct-faults example now keeps the discarded symptom in this field
instead of dropping it. In `RepairOrchestrator.HandleMessageAsync`: if
the primary search comes back with a literal `resultType == "not_found"`
and `secondarySymptom` was given, the exact same search re-runs
automatically with that text - deterministically in C#, no second
Gemini routing call, so no added cost/latency for the common
single-symptom case. Whichever result that produces (a real document/car
list, or a second `not_found`) becomes the turn's actual response.

Deliberately gated on a literal `not_found`, never on a low-confidence
match (section 6.19/6.20's `lowConfidenceMatch`) - those are a different
situation with their own ask-first flow, and stacking both behaviors in
one turn would produce two confusing prompts instead of one clear one.

Two new formatting rules in `SystemPromptBuilder.BuildFormatting`:
- **Rule 8d**: primary not found, secondary found - state plainly the
  first symptom had no document, then let the second's real result
  (rendered separately by the frontend) speak for itself.
- **Rule 9b**: both not found - name both attempted symptoms explicitly,
  then ask the existing Rule 9 clarifying questions (reused verbatim, not
  reworded).

**Verified, real chat flow, no mocks**:
- "il cambio slitta in terza marcia e sento anche rumore ai freni" (no
  car confirmed): response states no document was found for "cambio
  slitta terza marcia," then directly returns the real 32-car FORD
  C-Max selection for "rumore ai freni" - one turn, no extra
  confirmation step.
- "il cambio slitta in terza marcia e il bagagliaio non si chiude bene"
  (both genuinely absent from the catalog): "Nessun documento è stato
  trovato per 'cambio slitta terza marcia' o 'bagagliaio non si chiude
  bene'," followed by the standard Rule 9 questions.
- Regression: a normal single-symptom message ("la spia motore si
  accende e il veicolo perde potenza") still returns its real result
  with `message: null`, unaffected - no `secondarySymptom` ever set, no
  retry attempted.
- Regression: the low-confidence ask-first flow (confirmed IVECO Daily
  III 45C-13, asked about brakes) still asks correctly and isn't
  short-circuited by the new logic.

| Decision | Why |
|---|---|
| Deterministic C# retry, not a second Gemini routing call | The decision of *which* symptom is more specific is genuinely Gemini's job (language judgment); re-running the same deterministic search with known text is not - no reason to pay for another round-trip |
| Gated on literal `not_found`, never on `lowConfidenceMatch` | Different situations with different established UX (ask-first vs. auto-retry) - conflating them would double-prompt the mechanic in one turn |
| Reuse Rule 9's exact clarifying-question text in Rule 9b, just prefixed with both symptom names | Consistent with this session's existing discipline of not inventing new phrasing for what's structurally the same "nothing concrete to go on" situation |

---

### 6.23 Car-selection list: grouped by engine, sorted, filterable - scales
to the "thousands of cars later" case

**Why**: the flat grid (every matching car as an equally-weighted card, in
arbitrary query order) is already hard to scan once a result has 30+
cars sharing one brand+model (e.g. the FORD C-Max result from section
6.22's "rumore ai freni" fallback) - and the local sample dataset is
explicitly a stand-in for a much larger real catalog (section 1), so this
needed to scale before it became a real problem, not after.

**Design choice, picked from 3 mocked-up options via an explicit
preview-based question rather than guessed**: group by `motorizzazione`
(the engine label a mechanic actually recognizes, e.g. "1.5 TDCi 8v" -
the same field distinction `VehicleSearchService`/`FindCar` already make
between motorizzazione and the internal `codiceMotore`), not by brand or
model - most real results already share one brand+model, so grouping by
that would do nothing; grouping by engine is what actually splits a wall
of near-identical cards into scannable chunks.

**Built**: new `CarSelectionListComponent`
(`frontend/src/app/components/cards/car-selection-list/`), replacing the
inline `.car-grid` previously in `MessageBubbleComponent`:
- Groups `carMatches` by `motorizzazione` (a literal `"Altro"` bucket,
  sorted last, for the rare car with none at all), groups sorted
  alphabetically, cars within each group sorted by year descending then
  `codiceMotore` for a fully deterministic order - today's order was
  unsorted query order.
- Each group is a collapsible section (chevron + count), default
  expanded - collapsing is the mechanic's explicit choice, not a default
  that hides results.
- A filter box (plain substring match across marca/modello/
  motorizzazione/codiceMotore/alimentazione/year, all client-side - the
  full result set already arrives in one response, no backend change
  needed) - while filtering, matching groups force-expand regardless of
  collapsed state, so the mechanic never has to manually open a group to
  see whether their filter matched anything inside it.
- `app-car-card`'s own component and the click→`select` event wiring are
  completely unchanged - `CarSelectionListComponent` just re-emits the
  same `CarOption` object, so the existing click-to-confirm data binding
  (the exact thing the IVECO 5-trim regression test from earlier
  sessions exists to catch) couldn't be affected by this change.

**Verified, real Docker-composed stack, real Chromium via Playwright, no
mocks, both themes**:
- Grouped list renders correctly for the real 32-car FORD C-Max result -
  8 engine groups, correct counts, alphabetical order, cars sorted
  newest-year-first within each group.
- Collapsing a group hides its cards and rotates its chevron; other
  groups stay expanded and unaffected.
- Typing "2.0 TDCi" into the filter narrows to exactly the one matching
  group (5 cars, same sort order preserved).
- Clicking a card sends the exact correct confirmation request
  (`confirmedCarId`/`confirmedCodiceMotore`/`confirmedMarca` matching the
  clicked card precisely, verified via captured network request body,
  not just visually) - then the real confirmed-car header badge
  populates and Rule 7's replay + the low-confidence ask-first flow
  (section 6.20) both fire correctly for the newly confirmed car. One
  intermediate screenshot appeared to show an empty badge and an
  unrelated "need more details" reply - re-checked by polling for the
  actual new response instead of a fixed sleep, which confirmed this was
  purely a screenshot-taken-too-early script artifact, not a real bug;
  documented here precisely so a future session doesn't mistake the same
  artifact for a regression.
- Dark theme: same grouped list, correct token-based colors throughout,
  zero console errors.

| Decision | Why |
|---|---|
| Group by `motorizzazione`, not marca/modello | Most real results already share one brand+model; engine is what actually fragments a long result into distinct, scannable chunks |
| Client-side grouping/sort/filter, no backend change | The full result set already arrives in one response (Search/Vehicle Service return everything, no pagination exists) - no reason to add a round-trip for something already in memory |
| Default all groups expanded | Closest to today's existing behavior (nothing hidden by default); collapsing is the mechanic's choice, not imposed |
| Force-expand matching groups while filtering | A mechanic actively filtering for something specific shouldn't also have to manually open groups to see if their filter matched inside them |

---

### 6.24 Two real, independent language bugs found from one user report -
hardcoded Italian card labels, and a language-detector false positive

**Report**: a Portuguese-looking repair card showed Italian section
labels ("Impianto:", "Procedura di riparazione", etc.) around correctly-
Portuguese content. Investigated and found two separate, compounding
bugs, not one.

**Bug A - `RepairCaseCardComponent`'s structural labels were hardcoded
Italian.** `legend()` (the reliability-level explanatory text) was
already correctly localized via `caseSummary.language` - that's why the
legend body text rendered correctly. But every other label - "Impianto:",
"Dispositivo:", "Anomalia:", "Causa:", "Identificazione del sistema /
guasto", "Errori rilevati dall'autodiagnosi:", "Procedura di
riparazione", "Intervento", "Procedura", "Nota", and the reliability
heading itself - was a literal Italian string in the template, never
translated.

**Fix**: pulled the real per-language labels directly from the actual
`.resx` source files (`Data/resx_samples`) - the `<Capitolo>` chapter
titles for `Ordine` 1/3/4, and the `<B>label:</B>` markers inside each
chapter's `Corpo` - rather than inventing translations, same fidelity
principle as the existing `RELIABILITY_LEGEND`. Added a `LABELS` map
(IT/EN/FR/PT/ES) and a `labels()` getter, used throughout the template in
place of every hardcoded string.

**Bug B - independent root cause, confirmed via a fresh session (no
stale-language fallback possible): the language detector itself
misfired.** Sending "A motore caldo accensione spia avaria motore e
avaria generica" (grammatical, unambiguous Italian) on a brand-new
session produced `"language":"pt"` in the actual request payload.
`language-detector.ts` already had a `MIN_CONFIDENCE_RATIO` guard for
exactly this class of bug (added after an earlier real incident:
"Accensione spia avaria motore" scored pt/it ratio 1.14, below the
existing 1.3 floor) - but this new phrase, with more repetition of the
IT/PT-cognate words "avaria"/"motore", scored ratio **1.36**: still
wrong, and still above the existing 1.3 threshold meant to catch it.
Confirmed directly via `tinyld/light` outside the browser entirely
(`detectAll`), not assumed from the app's behavior alone.

**Fix**: recalibrated `MIN_CONFIDENCE_RATIO` from 1.3 to 1.6, using the
same real-data approach as the original threshold - tested both known-bad
phrases (1.14, 1.36) against a batch of genuinely correct, real
detections used throughout this project (IT/EN/FR/ES/PT), which all
scored 1.88 or higher. Clean gap, real margin on both sides, not a
guessed number.

**Verified live, real chat flow, both languages, no mocks**:
- The exact failing sentence now sends `"language":"it"` on a fresh
  session.
- A full Portuguese conversation (FindCar via symptom search → confirm
  IVECO Daily III 45C-13 → Rule 7 replay → low-confidence ask-first flow)
  rendered two real documents with fully Portuguese labels: "GRAU DE
  FIABILIDADE (*)", "IDENTIFICAÇÃO DO SISTEMA / AVARIA", "Sistema:",
  "Dispositivo:", "Causa:", "PROCEDIMENTO DE REPARAÇÃO", "Intervenção",
  "Nota" - and the low-confidence ask-first message itself rendered as
  natural, correct Portuguese ("Gostaria de vê-la de qualquer forma?").
  Worth noting: `LowConfidenceReason` (section 6.19) needed no separate
  fix - it's built from the matched document's own real, already-correct-
  per-language `Impianto`/`Dispositivo` field values, not invented text.
- Regression: a normal Italian flow still renders correctly with zero
  console errors.
- One real LLM non-determinism encountered during testing (the exact
  same request body, sent twice, produced a found result once and a
  false not-found once for a borderline-parseable "motor 8140.43S"
  phrase) - confirmed via identical captured request bodies, not a code
  regression; worked around by testing through a more reliable entry
  point (symptom search) rather than treating it as a bug to fix.

| Decision | Why |
|---|---|
| Real `.resx`-sourced labels, not invented translations | Same fidelity principle already applied to `RELIABILITY_LEGEND` and to backend document content - never guess what real source text says |
| Recalibrate the existing threshold rather than add a wordlist/stoplist for "avaria"/"motore" | The confidence-ratio approach already has a real, data-backed gap (1.36 max-bad vs. 1.88 min-good) - no evidence a per-word special case is needed yet |

---

## 7. Infrastructure / docker-compose

`docker-compose.yml` now has nine services: `our-postgres`
(pgvector/pgvector:pg16), `ingestion` (xlsx, depends on postgres healthy),
`ingestion-resx` (resx/graph/embeddings, depends on postgres healthy AND
`ingestion` completing successfully via
`condition: service_completed_successfully`), four long-running app
services, and `nginx`:
- `vehicle-service` — `restart: unless-stopped`, depends on postgres
  healthy AND `ingestion` completing (needs `gup_rows`).
- `search-service` — `restart: unless-stopped`, depends on postgres
  healthy AND `ingestion-resx` completing (needs graph + embeddings +
  `documents`), also gets `GEMINI_API_KEY` for query-time embedding.
- `chat-service` — `restart: unless-stopped`, depends on `search-service`/
  `vehicle-service` (plain `depends_on`, no `condition:` needed - they're
  long-running, not one-shot jobs). Gets `GEMINI_API_KEY` plus
  `SEARCH_SERVICE_URL`/`VEHICLE_SERVICE_URL` pointing at the internal
  service names (`http://search-service:5001`, `http://vehicle-service:5002`).
  No `OUR_DB` - Chat Service never touches Postgres directly.
- `frontend` — `restart: unless-stopped`, no `depends_on` (it's a static
  build; it never calls other services directly itself - the *browser*
  calls `/api/*` relative to whatever host served the page, which is
  nginx, not the frontend container).
- `nginx` — `nginx:alpine`, `restart: unless-stopped`, port `80:80`
  mapped directly in the main compose file (not the override file, since
  this is the one port meant to be exposed in any environment), mounts
  `nginx/nginx.conf` read-only, `depends_on` all four app services.

### nginx (API gateway) — WORKING, verified live

`nginx/nginx.conf` routes `/api/chat/*` → `chat-service:5000`,
`/api/search/*` → `search-service:5001`, `/api/vehicles/*` →
`vehicle-service:5002`, and `/` → `frontend:80`.

**Three real bugs found and fixed during live verification** (not
assumed correct from the config alone):
1. **Startup-crash risk**: before `frontend` existed as a compose
   service, `location / { proxy_pass http://frontend:80; }` would have
   crashed nginx entirely at startup (nginx resolves a *static*
   `proxy_pass` hostname at config-load time, not per-request) - worked
   around temporarily with a `return 200` placeholder, swapped back to
   the real `proxy_pass` once `frontend` was actually built (section 6.4).
2. **301 redirect on bare list endpoint**: the routes were originally
   plain prefix locations with trailing slashes
   (`location /api/vehicles/ { ... }`). nginx auto-redirects a request
   whose URI exactly equals the location prefix minus its trailing slash
   — so `GET /api/vehicles` (the real `[HttpGet]` vehicle-list route, no
   path segment) 301-redirected to `/api/vehicles/` instead of proxying
   directly. (`/api/search` and `/api/chat` have no bare-root route, so
   this was silent there.) Confirmed via live `curl -D -` showing the
   `Location` header and matching nginx access log line. Fixed by
   switching to regex locations —
   `location ~ ^/api/vehicles(/|$) { ... }` — which match the bare path
   directly while still correctly *not* matching an unrelated path like
   `/api/vehiclesXYZ` (verified both cases via curl after the fix).
3. **Bind-mounted config changes need an explicit nginx restart**: after
   wiring `frontend` in and flipping the `/` route to the real
   `proxy_pass`, `docker compose up -d --build frontend nginx` did NOT
   pick up the new `nginx.conf` content - docker compose only recreates a
   container on image/env changes, not on bind-mounted file content
   changes, so the old (already-running) nginx container kept serving the
   placeholder. The same `up` also recreated several backend containers
   (dependency-graph side effect of the compose file changing at all),
   which left the already-running nginx pointing at now-stale upstream
   IPs (nginx caches resolved IPs for static `proxy_pass` hostnames at
   its own startup, same root cause as bug #1) - manifested as a live
   `502 Bad Gateway` on `/api/vehicles/{id}` even though the service
   itself was healthy. Fixed with `docker compose up -d --force-recreate
   nginx`, run *after* the backend services had settled.

**Verified live against the real running stack (twice - once via
`ng serve`'s dev proxy, once via the full Docker-composed stack on
port 80):**
- nginx starts and stays up.
- `curl http://localhost/` → 200, the real Angular `index.html`.
- `curl http://localhost/api/vehicles?engineCode=KKDA` → 200, real car
  data.
- `curl http://localhost/api/vehicles/FO0010` → 200, real single-car
  lookup.
- `curl http://localhost/api/vehiclesXYZ` → 200, falls through correctly
  to the frontend's own SPA `try_files` fallback (proves the regex
  location isn't over-matching).
- Full chat flow (fault code → car selection → confirm → real repair
  document with correct `intervento`) driven in an actual Chromium
  browser via Playwright - see section 6.4 for the frontend-side detail.

`search-service`/`vehicle-service` connect via
`OUR_DB: Host=our-postgres;Port=5432;...` — the ADO.NET keyword format
Npgsql expects, **not** the `postgresql://...` URI format used by the
Python ingestion services' `OUR_DB` (same env var name, two different
formats, because the consuming driver differs). `docker compose up -d` is
now the single command to run the **entire** working stack, chat included.

`docker-compose.override.yml`: auto-merged by `docker compose up` (no
flag needed) — adds `our-postgres` port 5432→host (psql/DBeaver), plus
`search-service` 5001→host, `vehicle-service` 5002→host, and
`chat-service` 5000→host so Swagger/curl can reach them directly from the
host during development. Containers talk to each other over the internal
Docker network (`our-postgres:5432`, `search-service:5001`, etc.), not via
these host mappings.

**Verified after wiring `chat-service` in**: rebuilt with
`docker compose up -d --build chat-service`, confirmed `/health` and
`/swagger` both responded, then re-ran the full Rule 7 flow (fault code,
no car → car selection → confirm car → real document with full
`intervento` text) through the compose-managed container reaching
Search/Vehicle over the internal network - not the manually-run container
with hand-set env vars used during earlier development/testing. Same
result, confirming the compose wiring is equivalent to the test setup.

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
| **Italian naming standardization (Vehicle → Search → Chat/frontend, 3-stage consolidated entry)** | **Every English-named field/query-param touching car or engine identity, across all 4 services, renamed to match the codebase's existing Italian convention** (`Marca`, `Modello`, `CodiceMotore`, `AnnoInizio`/`AnnoFine`, `Alimentazione`) | **Deliberate deviation from `docs/SemaRepair_Architecture.md`** (sections 6.3/6.4/6.5 all specify English query params/fields, inconsistently alongside already-Italian response fields like `VehicleResult`/`CarSummary`/`CarOption`). Done in 3 stages, each fully live-verified before the next began: <br>**1. Vehicle Service** - `VehicleQuery`: `Brand→Marca, Model→Modello, YearFrom→AnnoInizio, YearTo→AnnoFine, Fuel→Alimentazione, EngineCode→CodiceMotore` (`Kw` unchanged; default `[FromQuery]` binding, no `Name=` override existed). `AnnoInizio`/`AnnoFine` as filter-range names are still correct under the overlap semantics (compared directly against `VehicleResult`'s same-named fields). <br>**2. Search Service** - `SearchRequest.Brand→Marca, EngineCode→CodiceMotore`; `DocumentResult.Title→Titolo` (confirmed real first: backed by the actual `titolo` DB column, not a doc-only field - `DocumentResult` and `CaseSummary` were both checked before renaming anything, and `CaseSummary` has no such field at all, so nothing to propagate there). **Real discovery**: `SearchRequest` was never actually bound by `SearchController` - its three endpoints take individual `[FromQuery] string? brand, string? engine` parameters directly, so `SearchRequest` was a documentation-only shape with zero live effect; the controller's own parameters were renamed too (the actual live contract), propagated through every internal helper (`SymptomWithCarAsync`, `TryFallbackAsync`, `BuildSharedEngineInfoAsync`, `GraphSearchService.ResolveCarIdsAsync` + its SQL parameter names). <br>**3. Chat Service + frontend** - `ChatRequest`/`Session`: `ConfirmedEngineCode→ConfirmedCodiceMotore, ConfirmedBrand→ConfirmedMarca` (`ConfirmedCarId` already correctly named from the earlier IVECO bug fix, left as-is); propagated through `RepairOrchestrator`'s change-detection, `ConfirmCarAsync`, `StoreConfirmedCar`, `BuildSearchUrl`, `BuildConfirmationFact`. **Second dead-code discovery**: `SessionStore.ConfirmCar(...)` also referenced the old field names but is never called anywhere (`RepairOrchestrator` manages `Session` fields directly) - renamed anyway for consistency rather than removed, since deleting unused-but-harmless code wasn't asked for. Frontend (`chat.models.ts`, `ChatStore.confirmCar`) updated to match: `confirmedEngineCode/confirmedBrand→confirmedCodiceMotore/confirmedMarca`. <br>**Held constant throughout all 3 stages**: `ToolDefinitions.cs`'s Gemini function-call argument names (`brand`, `engineCode`, etc.) and `SystemPromptBuilder.cs`'s prose - both are Gemini's own contract, independent of any HTTP query string or C# field name, and were never touched. <br>**Verified live at every stage** (re-grepped for the old names after each, zero matches outside `ToolDefinitions.cs`/historical log entries): `GetById` hit/miss, engine-code search, combined filters; fault-code/symptom/system endpoints including a real cross-brand Rule 8 fallback (CITROEN Jumper 4HV → FIAT Ducato sibling, `foundViaSharedEngine: true`) and the `TieThreshold` tie case; the full Chat Rule 7 *and* Rule 8 flows, including a re-run of the IVECO 5-trim disambiguation test (40C-13 specifically) through the real frontend in an actual Chromium browser via Playwright - the captured network request payload confirmed the real renamed field names (`confirmedCodiceMotore`/`confirmedMarca`) on the wire, not just passing builds. |
| Swagger | Added to Search/Vehicle, enabled unconditionally (not dev-gated) | User wanted an interactive way to see/test the APIs; neither service sits behind nginx yet, so there's no prod-exposure concern yet |
| Search/Vehicle compose wiring | Both added as real `docker-compose.yml` services with `restart: unless-stopped`, host ports via override file | No reason left to keep them out once they had real logic; matches the existing ingestion dependency-chain pattern |
| Search Type 3/4 result shape (revision) | Return all documents tied within `TieThreshold` (0.02 cosine distance) of the best match, not always exactly one | Live testing found real documents with byte-identical `anomalia` text → exact distance ties; picking one arbitrarily was an unstable, silently-wrong choice, not a real ranking result |
| Chat tool list | Added a 4th tool, `SearchBySystem`, beyond v1's 3 | Search Service already has a tested `/api/search/system` endpoint; excluding it would be an arbitrary gap |
| Chat session state | In-memory only, no persistence layer | Matches the doc's own unresolved Redis question; this is still a single-instance prototype |
| Rule 7 replay mechanism | Conversation `History` + a synthetic factual turn, NOT a deterministic `PendingSearch` field | Tested the riskier option directly instead of assuming: 15/15 live trials across two scenarios correctly re-issued the original search with the right text and engine code, with no explicit re-prompt |
| `engineCode`/`brand` in tool execution | Deterministically overridden from `Session`, never trusted from Gemini's own tool-call args | These are simple structured facts Chat Service already knows authoritatively - no reason to trust an LLM's echo of them, unlike free-text symptom/fault-code content |
| Chat document-content fidelity | Gemini never sees or produces document/car content - it's parsed directly from the raw Search/Vehicle JSON in code; Gemini only writes the short "message" framing text | Live testing found a real bug: the original design asked Gemini to reproduce document fields in its JSON output, and the result was missing `intervento` (the actual repair instructions) |
| Audio transcription mime type | Passed through as-is from the upload's `Content-Type`, no transcoding | No frontend exists yet to know what format it will actually send; verified working with a real WAV file, but browser recorders often produce `audio/webm`, which isn't in Gemini's documented supported list |
| Chat compose wiring | Added as a real `docker-compose.yml` service, same shape as search/vehicle, plain `depends_on` (no completion condition - it's long-running) | No reason left to keep it out once it had real logic; matches the existing pattern exactly |
| Swagger on Chat | Added, same as Search/Vehicle | Mainly useful for `/transcribe` (file upload UI); `/stream` is SSE and doesn't render usefully in Swagger's UI, so curl remains the practical test path for it |
| nginx `/` route | `return 200` placeholder, not `proxy_pass http://frontend:80` | `frontend` isn't a real compose service yet; a static `proxy_pass` to a nonexistent host crashes nginx at startup, not just on that one route |
| nginx API route matching | Regex locations (`^/api/vehicles(/\|$)`), not plain prefix locations with trailing slashes | Plain trailing-slash prefixes 301-redirect any request whose URI exactly equals the prefix minus the slash - broke the real bare `/api/vehicles` list endpoint; found via live curl, not assumed |
| nginx compose wiring | Added as a real `docker-compose.yml` service, port `80:80` in the main file (not the override file) | This is the one port meant to be exposed in any environment, unlike the dev-only direct service ports |
| **Frontend framework** | **Angular 19, not React** | Doc's section 6.2 specifies React; user explicitly rejected reusing the existing, complete v1 React frontend as a template and asked for a from-scratch Angular build instead - a deliberate deviation, not an oversight |
| Frontend SSE consumption | `fetch` + `ReadableStream`, not Angular's `HttpClient` | `HttpClient` has no native SSE support; the backend's `/api/chat/stream` events are discrete JSON objects (not a token stream), so this only needs line-buffered `data:` parsing, not a generic EventSource |
| Frontend session id | One `crypto.randomUUID()` per browser tab, held in a signal-based service, never persisted to storage | All real session state already lives server-side (`Session.cs`); persisting just the ID without the (lost-on-refresh) message list wouldn't help, so there was no reason to add storage |
| Frontend language scope (superseded, see row below) | Italian-only, no switcher, for this first build | Matches what was actually shippable in the time available; `ChatRequest.Language` already supports more server-side, left as a flagged follow-up rather than guessed at |
| Frontend language detection | Auto-detected per message via `tinyld/light` (restricted to it/en/fr/pt/es), not a manual dropdown | User explicitly asked for automatic switching, not a selector; a custom keyword heuristic was considered and rejected in favor of a small battle-tested detection library, since naive stopword matching is exactly the kind of "looks easy, has subtle edge cases" problem (especially distinguishing Spanish/Portuguese) already solved by existing libraries. `/light` variant used specifically to stay under the 500 KB bundle budget |
| Language fallback on ambiguous text | Falls back to the last successfully-detected language this session, not a hardcoded default | Short technical input (a bare DTC code, "si") frequently can't be confidently detected at all; re-defaulting to Italian on every such message would fight a mechanic mid-conversation in another language |
| Frontend voice scope | Voice input (record → transcribe → fill text box) only, no TTS playback | `chat-service` has no `/speak` endpoint (only `/transcribe`); v1 had a voice-mode toggle calling a TTS endpoint that doesn't exist in v2 - building one was out of scope for this pass |
| nginx `/` route (revision) | Flipped to the real `proxy_pass http://frontend:80;` | `frontend` now exists as a real, built compose service - the placeholder's reason for existing is gone |
| Car confirmation identity | Added `ChatRequest.ConfirmedCarId` (idMacchina) as the primary confirmation path; `ConfirmedCodiceMotore`/`ConfirmedMarca` (named `ConfirmedEngineCode`/`ConfirmedBrand` at the time - renamed later, see the consolidated Italian-naming entry above) demoted to a fallback for callers without a specific id | Real bug, reported with a full repro: one engine code (`8140.43S`) matches 5 IVECO Daily III trims - confirming by engine code+brand alone is inherently ambiguous, no query ordering fix would make it correct, only a specific id can |
| "New car confirmed" detection | Compares `ConfirmedCarId` first when present, falls back to comparing `ConfirmedCodiceMotore` only when no id was ever supplied | The old engine-code-only comparison would silently miss a mechanic switching between two trims that share one engine code - found while fixing the bug above, not separately reported |
| FindCar engine fields | Added `VehicleQuery.Motorizzazione` (ILIKE, wildcarded) as a real Vehicle Service filter, separate from the existing exact-match `CodiceMotore`; split `ToolDefinitions.FindCar`'s single `engineCode` Gemini arg into `motorizzazione` + `engineCode` with descriptions distinguishing them | Real bug, reported live: "FORD Ecosport... motore 1.5 TDCi 8v" returned "Nessun risultato trovato" even though 8 matching cars exist. Root cause: the mechanic's descriptive engine label ("1.5 TDCi 8v") landed in Gemini's `engineCode` arg (the tool only had one engine field) and was passed straight into Vehicle Service's exact-match `CodiceMotore` filter - which only ever holds internal codes like `XVJB`, never matching descriptive text. A mechanic realistically knows the descriptive label, essentially never the internal code |
| FindCar `fuel` field translation | `ToolDefinitions.FindCar`'s `fuel` param now explicitly instructs Gemini to translate into the database's 4 fixed values (`Diesel`/`Benzina`/`Gas`/`Benzina/Elettrico`), regardless of the mechanic's language; `BuildRouting` now explicitly carves this field (and `brand`) out of the routing prompt's general "never translate, pass verbatim" rule | Second bug found while re-testing the fix above in other languages, same symptom ("no results" for a real car): `gup_rows.alimentazione_macchina` only ever stores Italian-style values, but the prompt's blanket "preserve the mechanic's own words" instruction (correct for symptom/system text) was also being applied to this structured filter field - Spanish "diésel" (accent) and French "essence"/Portuguese "gasolina" (genuinely different words) never matched "Diesel"/"Benzina". Diagnosed by temporarily logging the actual Gemini tool-call args (no logging existed in this codebase before), since the same exact input was non-deterministically succeeding (~1/9 runs) or failing across repeated Spanish trials - the failure was real but intermittent, not visible from a single repro. Re-verified 8/8 (Spanish, car-only), 5/5 (Spanish, full original report), 3/3 each (French "essence", Portuguese "gasolina" against real Benzina cars) after the fix |
| Tailwind config style | v4 CSS-first (`@import 'tailwindcss'`, `@theme static`), no `tailwind.config.js` | Installed per Tailwind's current official Angular integration, not an older v3-era guide the user initially referenced (`darkMode:'class'` in a config file is v3-only syntax) |
| Dark mode mechanism | v4 `@custom-variant dark (&:where(.dark, .dark *));`, class-based only, no OS-preference fallback | User wants an explicit toggle as the single source of truth, persisted to `localStorage`, not following system preference |
| Dark-mode accent color | `#4773b4`, not the originally-proposed `#4d7dc4` | Real WCAG contrast calculation (not a visual judgment call, per explicit user requirement) showed `#4d7dc4` measured 4.162:1 against white text, failing the 4.5:1 AA floor; `#4773b4` measures 4.795:1 |
| Icon library | `@lucide/angular` (scoped), not `lucide-angular` as the user named it | `lucide-angular` is the superseded/legacy package; `@lucide/angular` is the actively-maintained one for current Angular - flagged before installing, per the user's explicit gate |
| Star/DTC-badge colors in dark mode | Per-theme Tailwind shades (`amber-700`/`amber-500` filled, `slate-500` empty), DTC badge unchanged | Measured contrast, not eyeballed: stock amber filled-star color failed even the 3:1 floor against the light surface (2.053:1) - a pre-existing bug, not introduced here; DTC badge's existing colors already cleared AA (6.367:1), so no dark variant was added there |
| `MessageListComponent` host display | `:host { display: contents; }` | Real bug found while verifying the fixed input bar: a component host defaults to `display:block`, so `flex:1`/`overflow-y:auto` on an *inner* div never reached the host actually sized by `.app-shell`'s flex layout - the whole document was scrolling, not the message panel, and the existing auto-scroll-to-bottom logic had been a silent no-op since before this redesign. `display:contents` makes the inner div the real flex item instead of adding new logic |
| Border CSS pattern | `border-width`/`border-style` (or `border-bottom-*`) only, never the bare `border: 1px solid;` shorthand | Real bug: the shorthand resets the omitted color sub-property to `currentcolor`, and Angular's view-encapsulation specificity let every such rule beat the `.border-border` utility class - every bordered element in the app (header, buttons, cards, bubbles) was rendering its border in the inherited text color, not the intended soft token, confirmed via `getComputedStyle` |
| `.theme-toggle`/`.reset-button` background rule | Removed the redundant `background: transparent;` | Same root cause as the border bug, one property over: it tied in specificity with `hover:bg-foreground/8` and won on source order, silently no-opping the hover effect on just these two buttons (others were unaffected since they never set `background` directly) |
| Hover tint opacity | `/8` (`hover:bg-foreground/8`), not the initially-tried `/5` | `/5` was confirmed applying via computed style but was visually imperceptible in a normal-scale screenshot - bumped until it actually read as a hover state, not just a technically-true CSS change |
| `--shadow-color` token | Plain custom property in `@layer base`, not inside `@theme` | Nothing generates a Tailwind utility from it (it's consumed directly via `rgb(var(--shadow-color))` in component CSS); putting it in `@theme` would make Tailwind treat the key as a named `--shadow-*` utility definition for no benefit |
| Shadow values per theme | Light: `15 23 42 / 0.08` (navy-tinted). Dark: `0 0 0 / 0.45` (much higher alpha, pure black) | A flat low-alpha black shadow reads fine on white but is nearly invisible on an already-dark surface - verified by rendering both, not reused as one value. Also fixed the Stage-3 input-bar shadow, which had been a flat `rgba(0,0,0,0.12)` applied identically in both themes without ever checking the dark case |
| Light-mode `surface` token | `#eef2f6`, not the original `#f8fafc` | Original measured 1.046:1 luminance contrast against `#ffffff` background - cards/panels visually blended into the page (confirmed by screenshot). `#eef2f6` measures 1.125:1 against background, 1.096:1 against `--color-border` (#e2e8f0) - visibly distinct from both background and its own border, without becoming the same shade as the border itself |
| `--color-bubble` / `--color-card-surface` split | Two new tokens instead of reusing `--color-surface` for either | `--color-surface` couldn't satisfy two opposite asks: message bubbles needed to read lighter (the Stage 6.6 surface bump made every bubble "too heavy"), car-cards needed to read more pronounced than the bubble they sit inside - one shared value can't move in both directions at once. `--color-surface` itself stayed unchanged for the components that weren't reported as a problem (repair-case-card, input-bar, confirmed-car badge) |
| `--color-card-surface` value | `#dde4ea` light / `#283750` dark, not the more visually-aggressive `#d9e0e8` candidate | The bolder candidate gave more visual separation (1.18:1 vs surface) but pushed `--color-muted` text contrast to 3.575:1; `#dde4ea` keeps it at 3.707:1 - both are below AA's 4.5 floor (so was the pre-existing surface baseline at 4.230:1), so the choice picked the smaller regression rather than pretending either was a clean pass |
| `.years` white-space | `nowrap`, `.car-meta` gained `flex-wrap: wrap` | Real bug: a year range like "2011–2014" wrapped mid-token at narrow grid-column widths since the span had no white-space override; nowrap forces it onto one line, and letting the *row* wrap instead (not the text inside any single span) keeps engine-code/power readable if the row no longer fits |
| FindCar "not found" specificity | `VehicleResponse.SuggestedYearFrom`/`SuggestedYearTo`, computed by a real second SQL query (Vehicle Service), not Gemini | Distinguishes "brand+model don't exist at all" from "they exist, just not for the requested year" - the latter needed a real fact (the actual production year range from `gup_rows`) the mechanic can act on, never a number an LLM might invent/approximate. Same fidelity principle as document content/car identity elsewhere in this build |
| Suggested-year fallback query trigger | Only runs when the original request had a year filter and returned 0 rows | A wrong brand/model with no year filter at all should stay a plain "not found" - running the extra query unconditionally on every 0-result search would be wasted work for no benefit, verified it doesn't engage in that case |
| FindCar not-found message templating | Built deterministically in `RepairOrchestrator.BuildChatResponse` from `VehicleResponse`'s real fields, not Gemini's formatting-call free text (which still runs but is discarded for this branch) | Same reasoning as the document-content-fidelity fix - the formatting call has no way to know it's looking at a Vehicle Service result, and letting an LLM paraphrase a real year range risks it stating a wrong or rounded number |
| `9999` end-year phrasing | Rendered as "to today" (per language), never "to 9999" | `anno_fine_macchina = 9999` means "still in production, no end year yet" in the data - stating that literally would read as a nonsensical year to a mechanic |
| `detectLanguage` minimum word count | `MIN_WORDS_FOR_DETECTION = 3` - below this, skip tinyld entirely and keep the session's prior language | Real bug, found from a live conversation: a single short reply ("0.9 TwinAir") scored a wrong language at the same confidence magnitude a genuine sentence scores for the right one - the accuracy number alone never separated a confident detection from a coin-flip on short input, only length reliably did |
| `BuildChatResponse`'s not-found branch gating | Added `call.Name == "FindCar"` as an explicit guard, not shape alone | Real regression from this session's own FindCar-not-found work: Search Service's fault-code/symptom/system responses always carry an empty `"cars": []` placeholder alongside a real `"documents"` array, so checking only for `cars`'s presence wrongly fired on successful searches too, discarding a real found document. 100% reproducible, including with the routing prompt fully reverted - proved this was a deterministic backend bug, not an LLM routing issue |
| FindCar routing-prompt wording (attempted, reverted) | Two attempts at making the existing FindCar/Rule-7 bullet more eager to call FindCar both broke Rule 7's replay mechanism 5/5 in testing; reverted to the bullet's exact original text | The "don't ask for fuel/engine/year before the first FindCar call" UX goal was real, but editing the same already-fragile, already-proven-reliable bullet was the wrong lever - kept as a separate, narrowly-scoped new bullet instead, which doesn't touch the original wording at all |
| Car-confirmation messages | Removed the frontend's synthetic "Confermo il veicolo: ..." user bubble and the backend's hardcoded "Veicolo confermato: ..." reply - both still happen internally (sent to the backend / added to `session.History`), just no longer shown to the mechanic | A live screenshot showed three messages for one confirmation - the two hardcoded ones were redundant with Gemini's own natural acknowledgment from the routing call that runs immediately after, which already had the same synthetic fact available to it |

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
- `nginx/nginx.conf` is now wired into `docker-compose.yml` and verified
  live against the real running stack (section 7) — no longer an open
  item. Swagger UI for each service is still only reachable on its direct
  host port (via the override file), not through nginx; only the
  `/api/*` routes are proxied.
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
- No retry/backoff for failed Gemini calls in Chat Service (section 9.1
  specifies 3 retries, 1s/2s/4s backoff) — currently fails straight to a
  fixed apology message on the first error.
- Chat's session state is in-memory/per-process — lost on restart, won't
  work if Chat Service ever scales beyond one instance.
- Audio transcription's mime-type handling is still untested against a
  real browser recording — the frontend now exists and wires up
  `MediaRecorder` → `/transcribe` (section 6.4), but the live browser
  verification done so far only exercised the typed-text path, not an
  actual microphone round-trip. Still verified only with a generated WAV
  file; `MediaRecorder`'s typical `audio/webm` output isn't in Gemini's
  documented supported list.
- Non-Italian `FindCar` calls are inherently less reliable than Italian -
  confirmed via repeated trials while diagnosing the `fuel`-translation
  bug above (Italian/English: 5/5 success; Spanish before the fix: ~1/9).
  The routing prompt's only concrete worked examples are in Italian; other
  languages rely on an abstract "same principle applies" instruction. Two
  real bugs (motorizzazione/engineCode conflation, fuel translation) were
  found and fixed this way, both by literally logging Gemini's tool-call
  args (no logging existed in this codebase before - added temporarily,
  removed after). Other structured fields/languages not yet stress-tested
  the same way could still have similar gaps - this is a methodology
  limitation (LLM tool-call extraction isn't deterministic), not something
  a single fix closes off entirely.
- Frontend has no TTS playback (section 6.4) - flagged as a deliberate
  first-pass scope cut, not a gap discovered later. (Language is no
  longer Italian-only - now auto-detected per message, see section 6.4.)

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
- **nginx:** verified against the real running compose stack, not just
  read for correctness. Three real bugs found this way and fixed (section
  7): a startup-crash risk from a static `proxy_pass` to a nonexistent
  `frontend` host, a 301-redirect bug on the bare `/api/vehicles` list
  route caused by plain trailing-slash prefix locations, and a stale-config/
  stale-upstream-IP bug after wiring `frontend` in (bind-mounted config
  changes don't trigger a container recreate; fixed with an explicit
  `--force-recreate`). No automated tests; verification was manual curl
  against the live container, including a deliberate negative test
  (`/api/vehiclesXYZ` correctly falling through) to confirm the
  regex-location fix didn't over-match.
- **Frontend (Angular):** built from scratch, no v1 code reused (a
  deliberate user decision, not a technical one). Verified twice in a
  real Chromium browser via Playwright - once against `ng serve`'s dev
  proxy, once against the actual Docker-composed stack on port 80 - not
  just `ng build` succeeding or a unit test passing. Full-page
  screenshots were taken and visually inspected at each step (initial
  load, car-selection cards, final repair-case card), not just DOM
  assertions. Zero browser console errors in either run. No automated
  tests exist yet (no Karma/Jasmine specs beyond the scaffolded
  `app.component.spec.ts`); the model types (`chat.models.ts`) were
  written by reading the actual C# response shapes directly rather than
  guessed from the architecture doc, which predates Chat Service's real
  contract - confirmed field-for-field against live SSE output before
  any component code was written.

---

## 11. Voice Mode — Phase 1

Voice mode (Phase 1) is implemented and pending live verification of the
Rule 8 cross-brand path. Everything else in Phase 1 is code-complete and
has been exercised through the UI.

### What was built

**Backend additions (both approved as scoped exceptions to DO-NOT-TOUCH):**
- `services/chat/Models/ChatResponse.cs` — added `FoundViaSharedEngine`
  to `CaseSummary` so the flag propagates to the frontend.
- `services/chat/Services/RepairOrchestrator.cs` `ParseCaseSummary()` —
  reads `foundViaSharedEngine` from the search JSON result and maps it to
  the new field. The flag originates in Search Service on every qualifying
  search call; RepairOrchestrator only threads it through.

**Frontend additions (all new files or modified files):**
- `frontend/src/app/models/chat.models.ts` — added `foundViaSharedEngine:
  boolean` to `CaseSummary` interface; added `lastResponse` signal and
  `detectedLanguage` signal to `ChatStore`.
- `frontend/src/app/services/speech/speech-engine.interface.ts` —
  `ISpeechEngine` abstraction (`speak`, `stop`, `isSupported`).
- `frontend/src/app/services/speech/web-speech.engine.ts` — Web Speech
  API engine. iOS returns `false` from `isSupported()` (blocked outside
  user gesture). Android Chrome chunking: ≤180 chars at sentence
  boundaries, `utterance.onend` chaining, `speechSynthesis.resume()`
  every 14s to defeat Android's cutoff bug.
- `frontend/src/app/services/speech/google-cloud.engine.ts` — Phase 2
  stub, always `isSupported()=false`.
- `frontend/src/app/services/speech/speech.service.ts` — injectable
  wrapper over both engines.
- `frontend/src/app/services/silence-detector.ts` — Web Audio
  AnalyserNode RMS detector. Constants: RMS_THRESHOLD=0.01,
  SILENCE_GRACE_MS=1500, SILENCE_STOP_MS=2000, NO_SPEECH_TIMEOUT_MS=8000,
  HARD_CAP_MS=30000.
- `frontend/src/app/services/voice-car-selection.service.ts` — spoken
  number parser (IT/EN/FR/PT/ES + digit forms); returns 0-based index or
  null on ambiguity/miss; used at §4.1 decision point.
- `frontend/src/app/services/voice-strings.ts` — §7 wrapper strings for
  all 5 languages. Functions: `t()`, `tCarSelectionPrefix()`, `tCarOption()`,
  `tFoundNCases()`. Covers car_selection, causa_prefix, intervento_prefix,
  found_n_cases, see_screen, all four toast keys.
- `frontend/src/app/services/voice-mode.service.ts` — state machine
  (idle→listening→transcribing→waiting_response→speaking→listening).
  `buildSpokenText()` is the sole source of TTS content — §5.8 order
  enforced; never calls Gemini; never rewrites causa/intervento.
- `frontend/src/app/components/chat/chat-input/chat-input.component.ts`
  and `.css` — two voice buttons (🔊 Web, ✨ HD stub), state-class
  animations (red pulse listening, amber pulse processing, green speaking),
  voice toast overlay.

### Rule 8 fix

**Bug:** `buildSpokenText()`'s Rule 8 guard originally fired on BOTH turns
of the cross-brand flow (flag-only check), causing the ask-permission
message to be spoken again on Turn 2 instead of the repair document.

**Root cause (confirmed from code reading, not live test):** Search Service
never resets `foundViaSharedEngine` — it is a property of the search
result, not session state. On Turn 2 after the mechanic says "sì", the LLM
issues the same search call and gets `foundViaSharedEngine=true` again.
`BuildFormatting` in `SystemPromptBuilder.cs` generates a non-null message
whenever the flag is true (its rule fires on the flag, not on whether the
mechanic has consented). No `confirmSharedEngineMatch` routing instruction
exists to suppress it (unlike `confirmLowConfidenceMatch` for Rule 8b).

**Fix (applied):** `sharedEngine && !hasRealDocument` — the guard only fires
on Turn 1 (causa absent = document not yet revealed). On Turn 2 the backend
has retrieved the actual document so causa+intervento are populated;
`hasRealDocument=true`, guard is false, §5.1 reads out the repair document.

```typescript
const sharedEngine = r.cases?.[0]?.foundViaSharedEngine === true;
const hasRealDocument = !!r.cases?.[0]?.causa && !!r.cases?.[0]?.intervento;
if (sharedEngine && !hasRealDocument) {
  return r.message ?? null;  // turn 1: disclosure + ask permission
}
// turn 2 and normal documents fall through to §5.1
```

### Rule 8 live test vehicle

**CITROEN Jumper 8140.43S (CI0046)** — initially selected as a test vehicle
but has its own documents in the graph; `foundViaSharedEngine` never fires.

**The correct test vehicle is CITROEN Jumper 4HV (CI2505):**
- 1 document (fuel pressure regulator, system: Alimentazione motore)
- Shares engine with FIAT Ducato 4HV (FI2515, 10 documents)
- For the `TryFallbackAsync` path to fire, need an **engine-related**
  fault code that FI2515 has but CI2505 doesn't.
  `SystemCategoryLookup.AllowsEngineFallback` gates on: Iniezione,
  Alimentazione carburante, Candelette, Sensori motore, Gestione motore,
  Alimentazione motore, Sistema di accensione, Sistema di scarico.
  C1050 (ABS/chassis code) doesn't qualify. B-prefix or P-prefix fuel/
  injection codes from FI2515's documents are the right class.
- The `SymptomWithCarAsync` fallback path also cannot fire here: CI2505 has
  1 document, so `candidateDocs.Count > 0` always; symptom Rule 8 requires
  `candidateDocs.Count == 0` (zero documents for the car in the graph).

### Phase 2 prerequisites (not started)

- Implement `google-cloud.engine.ts` fully (currently a stub)
- Add `POST /api/chat/tts` to Chat Service (server-side TTS proxy — the
  Google Cloud TTS API key must never be exposed to the browser)
- Add `GOOGLE_CLOUD_TTS_API_KEY` to docker-compose env
- iOS audio unlock pattern (Web Audio context needs a gesture to resume)
- Phase gate: Phase 1 live verification must pass before any Phase 2 file
  is created or modified

---

## 12. Voice Mode — UI/UX Polish

Voice mode Phase 1 UI/UX polish pass is complete and Playwright-verified.
Rule 8 live verification is still pending (see Section 12's pre-existing
instructions, now Section 13 below).

### What was built

**`silence-detector.ts`** — Added `get analyserNode(): AnalyserNode | null`
getter to expose the existing AnalyserNode to the visualizer without changing
any detection logic or creating a second AudioContext consumer.

**`voice-mode.service.ts`** (scoped exception to DO-NOT-TOUCH, approved for
presentation layer additions only):
- `readonly pendingTranscript = signal<string | null>(null)` — staging signal
  that holds the transcript while the typing animation plays. Replaced the
  direct `routeTranscript` + `state.set` calls in `onRecordingStop()` so
  §4.1 routing is deferred until the animation commits.
- `get analyserNode()` — forwards the SilenceDetector getter to components.
- `commitPendingTranscript()` — public method called by the component after
  animation completes; runs `routeTranscript`, advances state to
  `waiting_response`, clears `pendingTranscript`.
- `stopVoiceMode()` — added `pendingTranscript.set(null)` to safely discard
  an in-flight animation when the user cancels.

**`chat-input.component.ts`** (complete rewrite of the UI layer):
- Two `.voice-orb` gradient buttons replace the old plain icon buttons:
  Web Speech (sky blue) and HD Google stub (violet, disabled).
- `textSig = signal('')` replaces `text = ''` so animation writes are
  zone-safe without `NgZone.run()`.
- Two Angular `effect()` calls in the constructor:
  - Watches `pendingTranscript` → triggers `startTypeAnimation(transcript)`.
  - Watches `isActive()` → starts/stops the canvas RAF loop.
- `startTypeAnimation()`: 15ms/char setTimeout chain. On completion: 80ms
  pause, then `commitPendingTranscript()`, then 350ms before clearing the
  field (mechanic sees the text being routed before it disappears).
- `cancelTypeAnimation(skip)`: if `skip=true` — user tapped mid-animation —
  sets full text and commits immediately (skip-to-full).
- `onTextInput()`: user typing during animation → `cancelTypeAnimation(true)`.
- Canvas visualizer: reads `AnalyserNode.getByteFrequencyData()` each frame,
  draws 28 rounded bars (barW=3, gap=2), theme-aware color (blue-600 light /
  blue-200 dark), alpha modulated by amplitude. Self-terminates when
  `isActive()=false`.

**`chat-input.component.css`** (complete rewrite of the input/button area):
- `.input-area { flex:1; position:relative; overflow:hidden }` wrapper holds
  both the input and the canvas.
- `.viz-input` / `.viz-input--hidden` — text field fades out (opacity→0,
  pointer-events:none) while listening.
- `.voice-viz` / `.voice-viz--active` — canvas fades in over the input.
- `.voice-orb`: `radial-gradient(circle at 34% 34%, #f0f9ff → #0284c7)` —
  top-left highlight produces 3-D sphere appearance.
- `.voice-orb--hd`: violet variant (`#faf5ff → #9333ea`).
- `.voice-orb--listening`: `orb-pulse` keyframes (scale 1→1.08, expanding
  box-shadow ring, 1.7s).
- `.voice-orb--processing`: `orb-shimmer` keyframes (opacity 1→0.65, 1.1s).
- `.voice-orb--speaking`: swaps to green gradient + `orb-breathe` keyframes
  (box-shadow glow, 1.5s).
- `.orb-icon` fades to opacity:0 when any active state class is present.
- HD variants: `orb-pulse-hd` (violet ring) and `orb-breathe-hd`.
- Full dark mode coverage: `@media (prefers-color-scheme: dark)` +
  `:root[data-theme="dark/light"]` both handled.

### Typing animation rationale

Gemini transcription is a **single-call, complete result** — no interim
partials. The character-by-character type-in is purely presentational,
giving the perception of live typing. The implementation notes a documented
alternative: `webkitSpeechRecognition` with `interimResults=true` would give
genuinely incremental display on Chrome, with Gemini remaining authoritative
for the final commit. Not implemented — Chrome-only, adds a second STT
consumer.

The key timing constraint driving `pendingTranscript`: §4.1 routing (deciding
whether the transcript is a car selection or a query) must fire AFTER the
animation completes, not as soon as the transcript arrives. Without
`pendingTranscript`, there was no way to delay the routing without modifying
VoiceModeService's state machine internals (which are DO-NOT-TOUCH).

### Playwright verification results (all passing)

- 2 orbs found, both have `radial-gradient` backgrounds ✓
- Web orb: sky blue gradient, enabled ✓
- HD orb: violet gradient, disabled ✓
- Mic button: SVG icon present, no `voice-orb` class (visually unchanged) ✓
- Dark theme gradient: present ✓
- Listening state achieved: `voice-orb--listening` class applied ✓
- Visualizer canvas: `voice-viz--active`, opacity=1, size=492×41 ✓
- Input hidden while listening: `viz-input--hidden`, opacity=0 ✓
- Input visible again after stop ✓
- DOM structure: `.input-area`, `.viz-input`, canvas all present ✓
- Console errors: none ✓

---

## 13. Voice Mode — Phase 2 (Google Cloud TTS)

Phase 2 is complete and verified. The ✨ Voice HD button is live.

### What was built

**Backend: `POST /api/chat/tts`** (`ChatController.cs` + `GoogleCloudTtsService.cs`
+ `TtsRequest.cs` — additive only, §6.4 exactly):
- Calls `texttospeech.googleapis.com/v1/text:synthesize` server-side. The
  `GOOGLE_CLOUD_TTS_API_KEY` env var never leaves the backend.
- Voice map (single named constant): `it-IT-Neural2-A`, `en-US-Neural2-F`,
  `fr-FR-Neural2-A`, `pt-BR-Neural2-A`, `es-ES-Neural2-A`.
- HttpClient timeout = 10s (configured via `AddHttpClient<GoogleCloudTtsService>`).
- ALL error responses are JSON, never HTML:
  `empty_text` / `text_too_long` / `unsupported_language` (400),
  `tts_upstream_failed` (502), `tts_unavailable` (503),
  `tts_not_configured` (503 when key absent).
- Missing/empty key: service starts normally, all other endpoints unaffected.
- Structured log line per call: `chars={} lang={} ms={} status={}`.
- `docker-compose.yml` + `.env.example` updated with `GOOGLE_CLOUD_TTS_API_KEY`.

**Frontend: `GoogleCloudEngine`** (replaces Phase 1 stub in
`google-cloud.engine.ts`):
- `isSupported()` returns `true` unconditionally. Errors surface at
  `speak()` time (backed by the JSON error body from the endpoint).
- One reused `<audio>` element; previous blob URL revoked before each new play.
- `speak(text, lang)`: fetches `/api/chat/tts`, plays MP3 via the `<audio>`
  element, resolves on `'ended'`, rejects on any fetch/HTTP/playback error.
- `stop()`: `pause()` + revoke blob URL, idempotent.
- iOS audio unlock (`init()`, per §10.3): plays a minimal silent WAV (constructed
  as a 46-byte ArrayBuffer, valid PCM WAV) through the same `<audio>` element
  on the ✨ button tap (user gesture) before any async code runs.

**ISpeechEngine**: added optional `init?(): void` for the iOS unlock hook.
**SpeechService**: added `initForGesture(type)` — calls `engine.init?.()`.
**chat-input.component.ts**: injects `SpeechService`, calls
`speech.initForGesture(type)` inside the `toggleVoiceMode` handler
(still inside the click stack, before `startVoiceMode`).
**voice-strings.ts**: added `hd_voice_unavailable_toast` in all 5 languages.
**voice-mode.service.ts**: `handleResponseReady`'s `.catch()` now selects
`hd_voice_unavailable_toast` when `engine()==='google'` (§8 row 6), otherwise
`voice_unavailable_toast` (§8 row 5).

### nginx routing

`/api/chat/tts` routes through the existing `^/api/chat(/|$)` location — no
nginx change was needed. Confirmed by curl through port 80.

### Verification evidence

**Backend (via `http://localhost/api/chat/tts` — through nginx, port 80):**
```
# tts_not_configured (no API key in .env):
$ curl -s -X POST .../tts -d '{"text":"Ciao","language":"it"}'
{"error":"tts_not_configured"}  HTTP 503  Content-Type: application/json

# empty_text:
{"error":"empty_text"}  HTTP 400

# text_too_long (2001 chars):
{"error":"text_too_long"}  HTTP 400

# unsupported_language:
{"error":"unsupported_language"}  HTTP 400

# bad API key → Google rejects → 502:
{"error":"tts_upstream_failed"}  HTTP 502  Content-Type: application/json
```

**Frontend (Playwright, headless Chrome, fake mic):**
- 2 orbs present, both with `radial-gradient` backgrounds ✓
- HD orb (`voice-orb--hd`): `disabled=false` (enabled in Phase 2) ✓
- Mic button: SVG icon, no `voice-orb` class (unchanged) ✓
- HD orb click → `voice-orb--listening` class applied ✓
- Web orb disabled while HD active (only one engine at a time) ✓
- Visualizer canvas: `voice-viz--active`, opacity=1 ✓
- HD orb stop → returns to idle ✓
- Web orb re-enabled after HD stops ✓
- Web orb listening state (regression check): true ✓
- Console errors: none ✓

**Deviations from spec:**
- None. All §6.4, §10.3, and §9 Phase 2 requirements implemented exactly.

### Phase 3 prerequisites

- Phase 1 Rule 8 live verification is still pending (Docker/mic test with
  CI2505 CITROEN Jumper 4HV and a fuel/injection code from FI2515).
- Phase 3 (button animation refinement + demo prep) can start once Rule 8
  is verified and a real Google Cloud TTS API key is in `.env`.

---

## 14. TTS Live Fix — Root Cause, Gemini Model Audit, Transcribe Error Handling

### Root cause of TTS 502 / transcribe 500

The initial 502 from `/api/chat/tts` was diagnosed as a bad API key
(hex hash format). The real root cause emerged during key rotation: the
existing `GEMINI_API_KEY` was accidentally restricted to "Cloud
Text-to-Speech API only" in Google Cloud Console, which blocked all
`generativelanguage.googleapis.com` calls — causing the transcribe 500
with empty body. The fix:

- Created a **separate** `GOOGLE_CLOUD_TTS_API_KEY` restricted to Cloud
  Text-to-Speech API only (different project key, different credential).
- Left `GEMINI_API_KEY` unrestricted (Google AI Studio key, not Google
  Cloud Platform — these are different systems and cannot share a key).
- Both keys coexist independently in `.env` (gitignored).

### Gemini model audit — all services

Grep across all C# and Python services for experimental model strings:

```
grep -rn "exp|preview|experimental" --include="*.cs" services/ | grep -i gemini
grep -rn "gemini-" --include="*.cs" services/
grep -rn "gemini-" services/ --include="*.py"
```

**All model strings found — all stable:**

| File | Model | Status |
|------|-------|--------|
| `services/chat/Services/GeminiChatClient.cs` | `gemini-2.5-flash` | Stable ✓ |
| `services/search/Services/QueryEmbedder.cs` | `gemini-embedding-001` | Stable ✓ |
| `services/ingestion-resx/embedder.py` | `gemini-embedding-001` | Stable ✓ |

No `-exp`, `-preview`, or `-experimental` suffix found anywhere.
`gemini-2.0-flash-exp` (user's initial suspect) never existed in this
repo — it had already been replaced with `gemini-2.5-flash` before this
session.

### Transcribe endpoint — structured error handling

`POST /api/chat/transcribe` previously propagated Gemini exceptions
unhandled, producing a 500 with an **empty body** (nginx passed through
a zero-length response). The frontend's `fetch` + `res.text()` got an
empty string with no way to distinguish an API failure from a legitimate
empty transcript.

Fix: wrapped `GeminiChatClient.TranscribeAsync()` in a try/catch.
Success path stays `Content(transcript, "text/plain")` — unchanged
for the frontend's `res.text()`. Failure returns:

```
503 { "error": "transcription_unavailable", "detail": "<exception message>" }
```

Shape matches the TTS and QueryEmbedder error responses. A future Gemini
outage or model retirement now degrades gracefully with a parseable body
instead of a silent empty crash.

---

## 15. Voice Mode — Premium Restyle, Bug Fixes, and Button Polish (presentation-only)

**Scope: CSS, template bindings, and one Web Speech engine fix. Zero logic
changes to VoiceModeService, buildSpokenText, routing, or backend.**

### 15.1 Visual restyle — gradient orbs → quiet line-style buttons + input-bar glow

Replaced the saturated gradient orb buttons with a calm, minimal design
matching the mic button's own visual template.

**Buttons (both themes):**
- Before: large saturated `radial-gradient` balls (bright sky-blue / violet)
  with scale animations, pulsing glow rings, `LucideVolume2` icon.
- After: same 40px circle, `border-width: 1px; border-style: solid;`
  (structural CSS only — no `border` shorthand, same reason as all other
  bordered buttons in this app: shorthand would override `border-border`'s
  CSS variable). `border-border`/`text-foreground`/`hover:bg-foreground/8`
  Tailwind classes on the element supply all theme-aware colour. Active state:
  very light blue/violet tint (`rgba(59,130,246,0.07)` bg, blue-300 border,
  blue-500 icon for web; violet equivalents for HD). No `opacity` dimming
  on the idle state — matches the mic button pattern exactly.

**Input bar (new — breathing glow):**
- `input-bar--active` (listening/transcribing/waiting): soft blue `box-shadow`
  breathing on a 3.5s ease-in-out cycle. Ring pulses from 35% to 60%
  opacity blue-500 at 2→2.5px; ambient spread from 18% to 32% at 28→44px.
- `input-bar--speaking`: identical rhythm, slightly cooler indigo-500 tint.
- Idle: no glow — exactly the base `box-shadow` from §6.6.

### 15.2 Dark mode — critical diagnosis and fix

**Root cause of invisible glow in dark mode (and why initial selectors were wrong):**
This app's dark mode is controlled by a `.dark` class on `<html>` (Tailwind v4,
`styles.css` line 107: `.dark { --color-background: #0f172a; ... }`). It does
NOT use `data-theme` attribute or `prefers-color-scheme` OS media query.

Initial implementation used `:root[data-theme="dark"]` and
`@media (prefers-color-scheme: dark)` — both silently failed to match in this
app. Diagnosed by reading `styles.css` directly and confirming with a Playwright
diagnostic (base shadow changed correctly between themes, proving `.dark` class
is being toggled; `data-theme` attribute was never set).

**Fix for all dark-mode overrides:** all dark mode rules in
`chat-input.component.css` now use `:host-context(.dark)` — Angular's supported
way to match a class on any ancestor element. Applied to:
- `.input-bar--active` / `.input-bar--speaking` glow token overrides
- `.voice-btn--active` / `.voice-btn--hd.voice-btn--active` active tint overrides
- `.voice-toast` background override

### 15.3 CSS architecture — single keyframe + CSS custom properties

Angular scopes `@keyframes` names with the component attribute ID prefix
(`_ngcontent-ng-c3869154274_bar-breathe-light`). An initial design used two
named keyframes (`bar-breathe-light` / `bar-breathe-dark`) and tried to
switch between them in dark mode via specificity — this did NOT work, confirmed
by a Playwright diagnostic (`animationName` was `bar-breathe-light` in dark
mode regardless of which selector was applied).

**Working architecture:** a single `@keyframes bar-breathe` reads from CSS custom
properties (`--glow-ring-lo/hi`, `--glow-amb-lo/hi`). Dark mode overrides only
the property values via `:host-context(.dark)` — Angular scopes keyframe names
but NOT custom properties, so the properties switch correctly while the animation
name stays stable.

```
.input-bar--active    → defines --glow-* (light blue-500 values)
.input-bar--speaking  → defines --glow-* (light indigo-500 values)
@keyframes bar-breathe → reads --glow-* (single, shared)
:host-context(.dark) .input-bar--active   → overrides --glow-* (dark blue-300, higher opacity)
:host-context(.dark) .input-bar--speaking → overrides --glow-* (dark indigo-300, higher opacity)
```

### 15.4 Mic icon fix (lucide-mic → lucideMic)

The mic button was rendering a broken icon. Root cause: the old kebab-case
attribute name `lucide-mic` is the legacy `lucide-angular` API (the deprecated
package). The current `@lucide/angular` uses camelCase property binding:
`lucideMic`. Additionally, `LucideMic` was not imported in the component.

Fix: import `LucideMic` from `@lucide/angular`, add to `imports[]`, change
template attribute from `lucide-mic` to `lucideMic`. Removed unused
`LucideVolume2` import at the same time (voice orb buttons replaced with
`LucideAudioLines` / `LucideSparkles` in the restyle). File:
`frontend/src/app/components/chat/chat-input/chat-input.component.ts`.

### 15.5 Web Speech Italian voice fix

**Root cause:** `utterance.lang = 'it'` was set but `utterance.voice` was left
`null`. On Windows without an Italian TTS voice pack installed, the browser
ignores `lang` and falls back to the system default voice (English). The spec
says browsers *should* use `lang` to select a voice, but "should" is not
"must" and Windows Chrome behaves permissively here.

**Fix in `web-speech.engine.ts`:** explicitly call `speechSynthesis.getVoices()`
before speaking and assign the matching voice to `utterance.voice`:
- If `getVoices()` returns an empty array (voices not loaded yet): fall through
  with only `utterance.lang` set (browser may still pick correctly on some
  platforms, or the user may not have any TTS available yet).
- If voices are loaded but none match `lang`: reject with `no_voice_for_language`
  rather than silently speaking in the wrong language.

This fix is language-agnostic — it applies to all 5 target languages, not just
Italian.

### 15.6 Voice button visual match to mic button template

Final polish: updated `.voice-btn` CSS to exactly mirror `.mic-button`:
- Removed `border: 1px solid rgba(0,0,0,0.14)` (shorthand, hardcoded light
  colour, overrides `border-border` CSS variable).
- Removed `opacity: 0.5` (idle dimming not present on mic button).
- Removed `background: transparent` and `color: inherit` (Tailwind utilities
  handle these).
- Removed `:host-context(.dark) .voice-btn` border/opacity base overrides
  (they compensated for the old hardcoded values that no longer exist).
- Kept `.voice-btn--active` and `.voice-btn--hd.voice-btn--active` tint rules
  (still needed for the active engine indicator), and corresponding dark mode
  active-state overrides.
- Kept structural properties: `display`, `align-items`, `justify-content`,
  `border-width`, `border-style`, `border-radius`, `cursor`, `width`, `height`,
  `flex-shrink`, `transition`.

**Unchanged:** waveform visualizer, silence detector, mic button (§6.6 frozen),
send button, all VoiceModeService/buildSpokenText/routing/backend logic.

---

## 16. Rule 8 cross-brand voice path — two bugs found, fixed, and Phase 1 gate passed

**Phase 1 is now fully complete and verified.** This section documents what was
found during live verification, why the original design was wrong on two points,
and what the correct implementation is.

### 16.1 The two false assumptions in the original design

The original architecture comment said:

```
// Turn 1: backend returns foundViaSharedEngine=true, causa=null — disclosure
//         only, guard fires (hasRealDocument=false) → speaks r.message
// Turn 2: mechanic says "sì" → LLM issues same search call again → gets
//         foundViaSharedEngine=true, causa present → speaks causa+intervento
```

**Both assumptions were false.**

**Bug 1 — `causa` is never null on the disclosure turn.** The backend always
includes the full document (`causa`/`intervento`) in every response, even the
Turn 1 disclosure. This is correct for the non-voice UI (it shows the disclosure
text AND the full repair card together in a single turn). The `hasRealDocument`
guard in `buildSpokenText` — `if (sharedEngine && !hasRealDocument) speak
disclosure` — was *never* reachable. The real disclosed response (causa present)
always fell through to the found-document branch and spoke causa+intervento
immediately, bypassing the two-turn flow entirely.

**Bug 2 — Gemini does not re-search after "sì".** When a mechanic says "sì" as
a freestanding consent after the disclosure, Gemini generates a conversational
reply ("Perfetto, ecco i dettagli...") with no tool call. The stored document
never reaches the frontend a second time. The entire Turn 2 backend path was a
no-op at best, and a generic hallucinated response at worst.

Both bugs were confirmed live: sending P0380 for CI0037 (CITROEN Jumper RHV)
returned `foundViaSharedEngine=true` AND `causa='Fusibile F17'` in a single
response (bug 1 confirmed). Saying "sì" in a real browser session produced a
generic Gemini reply with no `/api/chat/stream` call replaying the document
(bug 2 confirmed).

### 16.2 The fix — frontend consent gate (no backend changes)

The fix lives entirely in `frontend/src/app/services/voice-mode.service.ts`.
No backend files were changed. `RepairOrchestrator.cs`, `SystemPromptBuilder.cs`,
and `SessionStore.cs` are unchanged.

**`handleResponseReady` intercepts before `buildSpokenText`:**
When `response.cases[0].foundViaSharedEngine === true`, the full response is
stored in `private pendingSharedEngineConsent: ChatResponse | null`. Only
`r.message` (the disclosure text) is spoken. The function returns early —
`buildSpokenText` is never called for this response.

**`commitPendingTranscript` checks the gate before routing:**
On the next transcript (the mechanic's "sì" or "no"):

```typescript
if (this.pendingSharedEngineConsent !== null) {
  const stored = this.pendingSharedEngineConsent;
  this.pendingSharedEngineConsent = null;
  if (isAffirmativeConsent(transcript, this.detLang())) {
    this.speakStoredConsent(stored);
    return;            // no backend call
  }
  // negative/unrelated: fall through to routeTranscript
}
this.routeTranscript(transcript);
```

**`speakStoredConsent`** reads `causa` and `intervento` from the stored response
and speaks them via the normal speech engine. No Gemini call, no rewrite.

**`buildSpokenText` Rule 8 guard** is simplified to `if (foundViaSharedEngine)
return r.message ?? null` — a safety net only, since `handleResponseReady` now
intercepts all shared-engine responses before `buildSpokenText` is reached.

**`isAffirmativeConsent`**: strict match at transcript start (`normalised === a
|| normalised.startsWith(a + ' ')`), per-language affirmatives plus a universal
set. Tolerant of trailing words ("sì grazie"). Anything non-affirmative routes
as a new request and discards the pending consent.

**`stopVoiceMode`** clears `pendingSharedEngineConsent = null` as part of
cleanup, so stopping voice mid-consent-wait never leaks the stored response.

### 16.3 Live verification — Playwright, 7/7 PASS

Test case: CI0037 (CITROEN Jumper RHV) + P0380 → fallback to FI0393
(FIAT Ducato RHV), doc 199310118, causa "Fusibile F17 (Protezione centralina
iniezione)".

| Assertion | Result |
|---|---|
| Real P0380 backend call fired, `foundViaSharedEngine=true` confirmed | PASS |
| `handleResponseReady` intercepted (consent stored or cleared before check) | PASS |
| "si" → 0 new `/api/chat/stream` calls, consent cleared, `speakStoredConsent` reached | PASS |
| "no" → consent cleared, "no" routed to backend as new request (1 API call) | PASS |
| "P0560" (unrelated) → consent cleared, fell through to `routeTranscript` | PASS |
| All `isAffirmativeConsent` edge cases correct (si/sì/certo/ok/yes/no/P0380) | PASS |
| Text mode (voice off): full card visible after P0380 (non-voice path unchanged) | PASS |

Angular 19 service access pattern used in tests: `ng.getComponent(document
.querySelector('app-chat-input')).voiceMode` (the `ng.getInjector()._records`
Map is empty in Angular 19 standalone — the old scratchpad script had used
this broken path).

**Phase 1 gate is fully passed. Phase 2 can begin.**


---

## 17. Numbered car selection — voice + text, all 30 slots, 5 languages

**Date:** July 2026

### 17.1 What was built

Mechanics can now select a car from the displayed list by number — by speaking
"otto", "8", "the eighth", "le huitième", etc., or by typing the number in the
text field. All car sizes (not just ≤5) are supported.

**Part 1 — Number badge on every car card**

Each card in the selection list shows a small badge (top-right corner) with its
visual position number (1, 2, 3 … N). Numbering is continuous across groups.
Badges render correctly in both light and dark themes via the `--color-muted`
token (auto-inverts under `.dark`). Collapsed groups do not break badge
correspondence — numbers are assigned from the flat sorted list regardless of
collapsed state.

**Part 2 — Shared selection parser (`selection-parser.ts`)**

A new pure-function utility `parseCarSelection(text, lang, listLength)` covers:
- Bare digits: "8", "21"
- Number words in all 5 languages (cardinals + ordinals 1–30):
  IT: uno…trenta, primo…ventesimo
  EN: one…thirty, first…twentieth
  FR: deux…trente, premier…dixième (skips "un/une" to avoid article false-positives)
  PT: dois/duas…trinta, primeiro…décimo
  ES: uno…treinta, primero…décimo
- Prefix words ("numero", "auto", "number", "car", "voiture", "coche", "carro",
  "veicolo") are non-number tokens — they're ignored naturally by the scanner,
  leaving only the number word to match.
- Compound 2-grams: "twenty eight", "dix sept", "vinte dois", "veinte uno"
- Compound 3-grams: "vingt et un", "vinte e um", "vinte e tres" (etc.)
- Greedy left-to-right n-gram matching prevents "vinte e quatro" from being
  split into two matches (20 and 4 → ambiguous) — the 3-gram "vinte e quatro"
  wins and yields 24.
- Ambiguity rule: multiple different numbers → null (route normally)
- Range rule: any number > listLength → null (route normally)
- Returns 0-based visual-order index, or null

**Part 3 — Text interception in ChatStore.sendMessage()**

Before sending to the backend, `sendMessage` checks `carDisplayOrder()`. If a
selection list is visible and the typed text parses as a valid number,
`confirmCar(carDisplayOrder[index])` is called and the text is never sent to
`/api/chat/stream`. This prevents Gemini's TooVague validation from rejecting
bare numbers while cars are pending.

**Part 4 — Voice path refactored**

`VoiceModeService.routeTranscript()` previously capped recognition at ≤5 cars.
The cap is removed. `VoiceCarSelectionService.parse()` now delegates entirely
to `parseCarSelection`. The route uses `chatStore.carDisplayOrder()` to map
index → car object and calls `confirmCar(car)` (not `confirmCarByIndex`) so
voice and the display always agree on which car is "number 8".

### 17.2 Single source of truth for visual order

New utility `frontend/src/app/utils/car-sort.ts` exports `sortCarsForDisplay`:
groups by motorizzazione (alphabetical, Altro last), within group by annoInizio
desc then codiceMotore asc. Both `ChatStore.carDisplayOrder` (computed signal)
and `CarSelectionListComponent.groups()` call this function, so badge number N
on card always equals index N−1 in the array the parsers use.

### 17.3 Files changed

| File | Change |
|---|---|
| `frontend/src/app/utils/car-sort.ts` | NEW — sortCarsForDisplay() |
| `frontend/src/app/services/selection-parser.ts` | NEW — parseCarSelection() |
| `frontend/src/app/services/voice-car-selection.service.ts` | Thin wrapper delegating to parseCarSelection |
| `frontend/src/app/services/chat-store.service.ts` | + carDisplayOrder computed + sendMessage interception |
| `frontend/src/app/services/voice-mode.service.ts` | routeTranscript: removed ≤5 cap, uses carDisplayOrder |
| `frontend/src/app/components/cards/car-card/car-card.component.ts` | + badge @Input, badge element in template |
| `frontend/src/app/components/cards/car-card/car-card.component.css` | + position:relative, .car-badge styles |
| `frontend/src/app/components/cards/car-selection-list/car-selection-list.component.ts` | Uses sortCarsForDisplay, passes badge per card |

### 17.4 §5.4 rule unchanged

`buildSpokenText()` still reads aloud ≤5 cars and says "too many" for >5.
This is independent of the recognition cap (which is now gone). Mechanics
can select by number regardless of whether the TTS read the list aloud.

---

## §18 — Multi-document compact card selection (2026-07-13)

When the backend returns 2+ repair cases the UI now shows a compact numbered
selection list instead of stacking full cards. Selection works via click, typed
number, or voice command. Expanding a card is fully reversible (back button).

### 18.1 Architecture

| Trigger | Path |
|---|---|
| Click compact card | `CaseSummaryCardComponent.select` → message-bubble `selectDoc` output → chat-page `handleSelectDoc` → `ChatStore.selectDocument(messageId, index)` |
| Type "2"/"secondo caso" | `ChatStore.sendMessage` → strict `parseCarSelection` → `selectDocumentInLastResponse(index)` |
| Say "due" | `VoiceModeService.routeTranscript` → `voiceCarSelection.parse` (strict=false) → `selectDocumentInLastResponse` → `speakCaseSummary` → `transitionToListening`; returns `true` (locally handled, no waiting_response) |
| Click back | message-bubble `clearDocSelection` output → `ChatStore.clearDocumentSelection(messageId)` |

### 18.2 selection-parser.ts strict mode

`parseCarSelection(text, lang, listLength, strict=false)` gains a 4th parameter.
`consumed: Set<number>` tracks token indices matched by the number scanner.
When `strict=true`, every non-consumed token is checked against `STRICT_CONTEXT`
(articles, vehicle nouns, doc nouns). Any real content word → returns null.

Examples with `listLength=5`:
- `"2"` → index 1 ✓
- `"il secondo caso"` → index 1 ✓ (il, secondo, caso all in STRICT_CONTEXT)
- `"ho 2 auto"` strict → null ✓ ("ho" is not in STRICT_CONTEXT)
- `"ho 2 auto"` non-strict → index 1 (voice path — mechanic just says "due", ok)

### 18.3 ChatStore additions

- `pendingDocSelection` — `computed<CaseSummary[] | null>`: cases array from most recent assistant message with `selectedCaseIndex == null` and `cases.length >= 2`
- `selectDocument(messageId, index)` — sets `selectedCaseIndex` on the message, returns CaseSummary
- `selectDocumentInLastResponse(index)` — finds the pending message, delegates to selectDocument
- `clearDocumentSelection(messageId)` — sets `selectedCaseIndex: null`
- `sendMessage` — intercepts car selection first (car wins if both somehow pending), then doc selection in strict mode; neither hits the backend

### 18.4 Voice §5.2 (buildSpokenText)

| Cases count | Spoken output |
|---|---|
| 1 | §5.1 unchanged: causa + intervento verbatim |
| 2–5 | `tFoundNCases` + numbered items `tCaseOption(i, dispositivo\|titolo)` + `case_selection_prompt` |
| >5 | `tCaseTooMany(n)` — "Ho trovato N casi. Guarda lo schermo e dimmi il numero." |

`routeTranscript` now returns `boolean`. `commitPendingTranscript` only sets
`waiting_response` when the return value is `false`. Doc-selection is locally
handled (true); car-selection and sendMessage both return false.

### 18.5 Files changed

| File | Change |
|---|---|
| `frontend/src/app/components/cards/case-summary-card/case-summary-card.component.ts` | NEW — compact card |
| `frontend/src/app/components/cards/case-summary-card/case-summary-card.component.css` | NEW — badge + layout |
| `frontend/src/app/services/selection-parser.ts` | + strict mode (consumed set, STRICT_CONTEXT, 4th param) |
| `frontend/src/app/models/chat.models.ts` | + `selectedCaseIndex?: number \| null` on ChatMessage |
| `frontend/src/app/services/chat-store.service.ts` | + pendingDocSelection, selectDocument, selectDocumentInLastResponse, clearDocumentSelection; sendMessage doc-interception |
| `frontend/src/app/services/voice-strings.ts` | + case_option, case_selection_prompt, case_too_many; updated found_n_cases (removed trailing "Il più rilevante"); + tCaseOption, tCaseTooMany exports |
| `frontend/src/app/services/voice-mode.service.ts` | buildSpokenText §5.2 rewrite; routeTranscript returns boolean + doc-selection branch; speakCaseSummary; commitPendingTranscript uses boolean |
| `frontend/src/app/components/chat/message-bubble/message-bubble.component.ts` | Conditional rendering (1 doc / expanded / compact list); +selectDoc/clearDocSelection outputs; selectedCase getter; backLabel |
| `frontend/src/app/components/chat/message-bubble/message-bubble.component.css` | + .doc-list, .back-btn |
| `frontend/src/app/components/chat/message-list/message-list.component.ts` | +selectDoc, +clearDocSelection outputs; passes through from message-bubble |
| `frontend/src/app/components/chat-page/chat-page.component.ts` | handleSelectDoc; wires all 3 doc-selection events |

---

## 19. Backend routing fix + determinism hardening (2026-07-14)

Three separate bugs found and fixed in a single diagnostic session triggered by "Problemi iniezioni" returning no results while "Guasto sistema di iniezione" worked.

### 19.1 Root cause of "Problemi iniezioni" → no results

Diagnosis from real logs: Gemini routed "Problemi iniezioni" to `SearchBySystem(name="iniezioni")` instead of `SearchBySymptom`. The routing prompt said "call SearchBySystem when the mechanic names a system **without describing a fault**" but gave no examples of fault words, so Gemini extracted "iniezioni" as the system name and ignored "problemi".

The `SearchBySystem` path uses `ILIKE @keyword` (exact case-insensitive match). Graph stores `Iniezione` (singular); Gemini passed `iniezioni` (plural). Neither is a substring of the other — the query returned 0 results.

"Guasto sistema di iniezione" routed to `SearchBySymptom` → vector embedding → found 6 docs (F1AE0481D) / 4 docs (8140.43S).

**Fix:** Added explicit fault-word rule to `SystemPromptBuilder.BuildRouting()` with all 5 languages' fault vocabularies and positive/negative examples. Only routing prompt change — no SQL or search logic touched.

Fault words added (all 5 languages):
- IT: problemi, problema, guasto, errore, anomalia, avaria
- EN: problem, problems, fault, error, issue, failure
- FR: problème, problèmes, panne, erreur, anomalie, défaut
- PT: problema, problemas, avaria, erro, anomalia, falha
- ES: problema, problemas, avería, error, anomalía, fallo

### 19.2 Non-determinism in search results ("4 docs vs 5 docs")

Observed: same mechanic input on same car returned different document counts across runs. Diagnosed from 27 real cosine distances (8140.43S, "Problemi iniezioni"):

```
Rank | id_documento | dist    | gap from best
  1  | 199309715    | 0.25050 | 0.00000  ← INCLUDED (TieThreshold=0.02)
  2  | 199309871    | 0.26086 | 0.01036  ← INCLUDED
  3  | 199309732    | 0.26329 | 0.01279  ← INCLUDED
  4  | 199310191    | 0.26422 | 0.01372  ← INCLUDED
  5  | 199309706    | 0.27977 | 0.02927  ← EXCLUDED — 0.00927 past threshold
```

Doc #5 is 9.3mm outside the 20mm TieThreshold window. A ±0.01 shift in the best distance (from slightly different Gemini cleaning output) crosses this boundary. This is a **known characteristic, not a bug** — anyone seeing result counts change after a prompt tweak should check cosine distances.

### 19.3 Temperature was never set — affected every LLM decision in the system

`GeminiChatClient.GenerateAsync` sent no `temperature` field for its entire existence. Every routing call, formatting call, and transcription call was running at gemini-2.5-flash's default (non-zero) temperature. This is the root cause of the 4-vs-5 result variation: Gemini's routing call cleaned "Problemi iniezioni" to slightly different `symptom` text on different runs, producing different embedding vectors.

**Fix:** `temperature` and `disableThinking` added as per-call parameters to `GenerateAsync`. Each call site opts in independently:

| Call | temperature | disableThinking | Rationale |
|---|---|---|---|
| Routing | 0 | true | Mechanical classification: which tool, which args. No creativity needed. |
| Formatting | default | false | Conversational prose. Slight variation harmless, thinking helps quality. |
| Transcription | default | false | Audio → text. No structured output needed. |

### 19.4 Why temperature=0 alone was not enough (thinkingBudget=0 required)

gemini-2.5-flash is a thinking model. Setting `temperature=0` controls the output token sampling but NOT the thinking chain. The thinking tokens are sampled independently — a non-zero internal temperature during the "thinking" phase means the reasoning chain can still vary run-to-run even when final output tokens are greedy. Observed: with only `temperature=0`, 3 of 8 runs still produced NO-CALL responses (Gemini's thinking led it to "clarify" rather than call a tool). After adding `thinkingBudget=0` (disables extended reasoning entirely), all 5/5 runs were identical.

The routing call is purely structural (classify → call tool → extract args). Thinking is appropriate for the formatting call (prose quality benefits from reasoning about tone, completeness, etc.) but counterproductive for routing.

### 19.5 SQL ORDER BY missing tiebreaker (ConfirmCarAsync precedent)

Both vector SQL queries had `ORDER BY dist` with no stable tiebreaker. With 108 documents in the sample, no two docs currently land at the same cosine distance — but this is luck. With production data (many more documents), distance collisions become likely, and `ORDER BY dist` without a tiebreaker + a LIMIT returns an arbitrary document when two tie. This is the identical class of bug as the ConfirmCarAsync IVECO issue (no ORDER BY → "whatever row PostgreSQL scanned first").

**Fix:** Both queries changed from `ORDER BY dist` to `ORDER BY dist, id_documento`.

### 19.6 Verification results (post-fix)

All 3 sequences ran 5 fresh sessions each with temperature=0 + thinkingBudget=0:

| Test | What | Expected | Result |
|---|---|---|---|
| A | "Problemi iniezioni" / 8140.43S, 5 runs | tool=symptom, q=[problemi iniezioni], docs=4, same IDs | **5/5 ✓** |
| B | "Iniezione" alone / 8140.43S, 5 runs | tool=system, q=[Iniezione], docs=3, same IDs | **5/5 ✓** |
| C | "Problemi iniezioni" / F1AE0481D (Rule 7 regression), 5 runs | tool=symptom, same count | **5/5 ✓ (8 docs each run)** |

### 19.7 Files changed

| File | Change |
|---|---|
| `services/chat/Services/SystemPromptBuilder.cs` | + fault-word rule, 5-language vocabulary, positive/negative routing examples |
| `services/chat/Services/GeminiChatClient.cs` | + `temperature` and `disableThinking` per-call params; `ThinkingConfig` inner class |
| `services/chat/Services/RepairOrchestrator.cs` | routing call now passes `temperature: 0, disableThinking: true` |
| `services/search/Services/VectorSearchService.cs` | `ORDER BY dist` → `ORDER BY dist, id_documento` |
| `services/search/Services/SymptomSearchService.cs` | `ORDER BY dist` → `ORDER BY dist, id_documento` |

## 20. Routing-prompt contradiction fixed: word-count rule replaced with a specificity rule (2026-07-15)

### 20.1 The contradiction

`SystemPromptBuilder.BuildRouting()` contained both a rule and a worked example that directly disagreed with each other, in the same prompt:

- Rule (Symptom cleaning rules section): "Minimo 3 parole tecniche" (minimum 3 technical words).
- Example (Tool routing section, §19.1's own fault-word-list fix): `"Problemi iniezioni" → SearchBySymptom(symptom="problemi iniezioni")` — 2 words.

Gemini had no way to reconcile these — a rule and an immediate violation of that rule, both presented as authoritative. This is exactly the kind of prompt-internal disagreement that produces the run-to-run unpredictability §19 was written to eliminate.

### 20.2 Word count was the wrong proxy for specificity

"problemi iniezioni" (2 words) names a system precisely. "la macchina non va bene" (5 words) names nothing. The word-count rule accepted the vague one and rejected the specific one — backwards, because word count was never actually measuring specificity; it was a shortcut standing in for it.

### 20.3 The fix: judge concreteness, not word count

The "Minimo 3 parole tecniche" line was replaced with a rule that asks whether the description names something concrete — a system, a component, a warning light, or a specific observable behavior — regardless of how many words that takes. "È vaga solo quando dice che qualcosa non va SENZA dire cosa o dove" (it's vague only when it says something's wrong without saying what or where). No minimum or maximum word count appears anywhere in the prompt anymore.

### 20.4 The fault-word list (added in §19.1) was also removed — it had the same flaw

§19.1's fix was a closed, per-language vocabulary list (`problemi, problema, guasto, errore, anomalia, avaria`, ×5 languages) used to decide "system name alone → SearchBySystem" vs "system name + fault word → SearchBySymptom". It was already known to be incomplete: "iniettori rotti" has no listed fault word ("rotti" isn't on the list), so it would have silently fallen through. This is the identical shortcut-for-a-judgment problem as the word-count rule, just implemented as vocabulary matching instead of counting.

Replaced with the same concreteness test used in §20.3: a bare system/device name → SearchBySystem; a system/device name plus ANY indication something's wrong, in any wording, not limited to a fixed list → SearchBySymptom. One mechanism now governs both decisions instead of two separate, potentially-conflicting ones — this was a deliberate choice to avoid re-creating the exact kind of contradiction this section started from.

### 20.5 A second gap found and fixed during verification (not in the original ask, found by testing)

Verifying "la macchina va male" and "it doesn't work" against the §20.3 fix alone showed the concreteness judgment wasn't yet applied to the earlier decision of whether to call a tool *at all*:

- "la macchina va male" → Gemini called `SearchBySymptom(symptom="la macchina va male")` and returned 3 real (irrelevant) documents, instead of asking for clarification.
- "it doesn't work" → Gemini called `SearchBySymptom` and landed on a low-confidence-match fallback (Rule 8b), instead of asking for clarification.

Both are technically-defensible-looking but wrong outcomes: the model was willing to attempt a search on text that names nothing concrete, just because it was Not Obviously Empty ("non funziona" — 2 nearly-identical words — was correctly recognized as too vague to search; a longer sentence built entirely from filler was not).

Added an explicit rule, positioned first in Tool routing (before the DTC/system/symptom bullets): if the message names nothing concrete, call no tool at all and ask a short clarifying question directly, using the same concreteness test as §20.3/20.4. Explicitly states that word count is not the test, so a longer-but-still-empty sentence doesn't get treated as more searchable than a short one.

### 20.6 Verification results (5 runs each, fresh sessions, confirmed car 8140.43S/FIAT — id `FI0398`, live Gemini calls, real DB)

| # | Input | Language | Expected | Result |
|---|---|---|---|---|
| 1 | "Problemi iniezioni" | it | SearchBySymptom, docs found | **5/5** — `symptom=problemi iniezioni`, 4 docs every run |
| 2 | "iniettori rotti" (not on old fault-word list) | it | SearchBySymptom | **5/5** — `symptom=iniettori rotti`, 4 docs every run |
| 3 | "iniettore difettoso" | it | SearchBySymptom | **5/5** — `symptom=iniettore difettoso`, 3 docs every run |
| 4 | "Iniezione" alone | it | SearchBySystem (no over-correction) | **5/5** — `name=Iniezione` |
| 5 | "non funziona" | it | Rule-9-style clarification, no tool call | **5/5** — no tool call every run |
| 6 | "la macchina va male" | it | Rule-9-style clarification, no tool call | **5/5** after §20.5 fix (was 5/5 *wrong* — real search — before it) |
| 7 | "injection problems" / "injectors broken" / "problemes d'injection" | en / en / fr | SearchBySymptom | **5/5 each** (15/15 total) |
| 8 | "it doesn't work" | en | Rule-9-style clarification, no tool call | **5/5** after §20.5 fix (was 5/5 *wrong* — low-confidence fallback — before it) |

Every run used a fresh session against the real local stack (chat-service → search-service/vehicle-service → Postgres with pgvector, real Gemini API calls) — no mocking.

### 20.7 A separate, pre-existing gap found but NOT fixed here (flagging, out of scope)

`ValidationService.ValidateSymptom` (search-service, Rule 12 — see docs/EmbeddingAndGraph_Technical.md) gates vagueness server-side with a hardcoded 6-word Italian stopword list (`problema, errore, guasto, non, funziona, rotto`) plus a "fewer than 2 words" floor. Tested directly: this gate would **not** have caught "va male" as vague (2 words, neither is on the stopword list) if Gemini had called `SearchBySymptom` with that text — which is exactly what was happening before the §20.5 fix. It's the same word-list-proxy-for-meaning flaw as §20.1–20.4, just server-side instead of in the prompt.

Not touched this session — different file, not part of what was asked, and currently masked in practice by §20.5 (Gemini no longer calls the tool for this kind of input at all). Flagging it because the mask is only as good as the prompt's own judgment: if routing ever regresses, or a future caller reaches `SearchBySymptom` without going through this prompt (a different client, a retry path, etc.), this backend gate would silently let vague text through as a real search. Worth a follow-up pass if that surfaces.

### 20.8 Files changed

| File | Change |
|---|---|
| `services/chat/Services/SystemPromptBuilder.cs` | Removed "Minimo 3 parole tecniche" word-count rule and the closed fault-word vocabulary list; replaced both with one concreteness/specificity judgment; added an explicit "name nothing concrete → call no tool, ask directly" rule |

### 20.9 Follow-up check: is a closed fault-word list still needed anywhere? No — already gone

A later prompt asked to re-verify (before doing any more work) whether the closed fault-word vocabulary from §19.1 was still present and still being matched against — the concern being that §20.3's specificity rule only helps if the old list was actually removed, not left in place alongside it as a second, potentially-conflicting mechanism.

Checked by reading the current prompt text directly: the list is gone (removed in §20.4) and the routing rule now reads "not limited to a fixed list of 'fault words'" with no enumerated vocabulary anywhere. §20.6's existing results already cover the specific regression this check was worried about — "iniettori rotti" and "iniettore difettoso" (§20.6 rows 2–3) were never on the old list and both routed to `SearchBySymptom` 5/5, and the non-Italian equivalents (§20.6 row 7) passed 5/5 each. No code change made; nothing further was needed.

## 21. Boundary-tie fix — live end-to-end verification through the real stack (2026-07-20)

The boundary-tie handling in `SymptomSearchService.FindBestMatchesAsync` (the `limit+1` probe + epsilon re-query + `LogInformation` line, added earlier) had only ever been exercised via a standalone console harness that instantiated the service directly. This section records the full verification through the **real running Docker stack** (nginx → search-service → Postgres/pgvector, live Gemini embeddings), following the handoff §5 checklist. No source code was modified; the only file changed is this one. The boundary case was reachable on local data, so this is a **complete** verification, not the reduced-limit fallback.

### 21.1 Why 199309673 / 199309676 are the test case

These two documents carry byte-identical `anomalia` text in it/en/fr/pt, so their stored `symptom_embeddings` vectors are identical and the cosine distance **between them is exactly 0** (measured live; `es` differs at 0.0136 due to a slightly different Spanish wording):

```
 language | dist_673_to_676
----------+-----------------
 en       | 0
 fr       | 0
 it       | 0
 pt       | 0
 es       | 0.013585872387191888
```

Because they are a perfect tie, they always occupy two adjacent ranks at the identical distance to any query. If that adjacent pair straddles the `LIMIT` cutoff, a plain `ORDER BY dist LIMIT n` keeps one and silently drops the other — the exact bug the fix exists to prevent.

### 21.2 Step 1–2: positioning the tie at the LIMIT=5 boundary (iterative query-crafting)

The default `limit` for the no-car path (`SymptomWithoutCarAsync` → `FindBestMatchesAsync`) is 5, so the tie has to land at ranks 5–6. Query text was embedded with the **same** params the service uses (`gemini-embedding-001`, `task_type=RETRIEVAL_QUERY`, 768 dims) and the resulting vector ranked against `symptom_embeddings` in psql. Several rounds of adjustment were needed because the "loss of performance + engine warning light" cluster is very tight (~0.15–0.16). One extra constraint surfaced mid-iteration: the query must contain **no** system/device name (e.g. "Quadro strumenti" is a real device name), or the endpoint's `MatchSystemOrDeviceAsync` narrows the candidate set and the full-table ranking no longer applies. The winning query avoids all 70 Italian system/device names:

**Query (lang=it):** `Notevole calo di prestazioni e potenza con accensione spia avaria motore sul cruscotto`

Full-table ranking (psql, live query vector):

```
 rank | id_documento |   dist   |     mark
------+--------------+----------+---------------
    1 | 199310573    | 0.151807 |
    2 | 199309703    | 0.152774 |
    3 | 199310553    | 0.158678 |
    4 | 199310207    | 0.161831 |
    5 | 199309673    | 0.162261 | <== TIED PAIR
    6 | 199309676    | 0.162261 | <== TIED PAIR
    7 | 199310614    | 0.162421 |   (strictly greater — clean boundary)
    8 | 199310368    | 0.163225 |
```

Exactly 4 distinct documents rank ahead of the pair; ranks 5 and 6 share the identical distance 0.162261; rank 7 is strictly greater (0.162421). This is the precise boundary straddle.

### 21.3 The bug the fix prevents (pre-fix vs probe, shown directly in psql)

```
--- plain LIMIT 5 (pre-fix behaviour): rank 6 of the tied pair is SILENTLY DROPPED ---
 199310573 | 0.151807
 199309703 | 0.152774
 199310553 | 0.158678
 199310207 | 0.161831
 199309673 | 0.162261   <== TIED  (199309676 is gone — it was rank 6)

--- LIMIT 6 (the limit+1 probe the fix uses): both tied rows visible at 0.162261 ---
 ... same 4 ...
 199309673 | 0.162261   <== TIED
 199309676 | 0.162261   <== TIED
```

With `id_documento` as the deterministic tiebreak, plain `LIMIT 5` keeps 673 (lower id) and drops 676 with no signal. The fix's `limit+1` probe sees both share the cutoff distance, so it re-queries for every document at or below the cutoff and returns the whole tied set.

### 21.4 Step 3–4: real endpoint + log line

`GET http://localhost:5001/api/search/symptom?q=<query>&lang=it` (no `codiceMotore` → Search Type 4, full-table) → **HTTP 200**. The search-service container log emitted the re-query line, with the distance matching the psql prediction to full precision:

```
Boundary tie detected at distance 0.16226141730443222; re-query returned 6 tied documents
```

(The count is 6 because the re-query returns every document at or below the cutoff — ranks 1–6 — which it then merges and dedupes; the load-bearing fact is that both 673 and 676 are in the returned set instead of 676 being dropped.)

### 21.5 Step 5: 5× determinism

Five fresh calls to the same endpoint:

```
run 1: HTTP 200  bodyMD5=69c26fb9e86a439ddd79fb087c07081b
run 2: HTTP 200  bodyMD5=69c26fb9e86a439ddd79fb087c07081b
run 3: HTTP 200  bodyMD5=69c26fb9e86a439ddd79fb087c07081b
run 4: HTTP 200  bodyMD5=69c26fb9e86a439ddd79fb087c07081b
run 5: HTTP 200  bodyMD5=69c26fb9e86a439ddd79fb087c07081b
```

- All 5 response bodies byte-identical (1 distinct MD5).
- The "Boundary tie detected" log line fired exactly 5 times (once per call), all at the identical distance `0.16226141730443222`.
- Endpoint response shape: `resultType=car_selection, count=26` — Type 4 merges the car sets of every tied top document (both 673 and 676 contribute), never surfacing the document itself pre-confirmation (Rule 1 intact).

### 21.6 Verdict

**Mechanism verified end-to-end through the real stack, boundary reachable on local data.** The tied pair 199309673/199309676 was positioned exactly at the LIMIT=5 boundary (ranks 5–6, identical distance 0.162261, rank 7 strictly greater); the re-query fired and returned both tied documents where a plain `LIMIT 5` would have dropped 199309676; behaviour is fully deterministic across 5 runs. `VectorSearchService` is unaffected (it has no `LIMIT`, so it cannot truncate a tie and needs no equivalent fix). No source code was changed during this verification.

### 21.7 Negative check: the re-query must NOT fire on normal queries

The positive test proves the re-query fires when a tie straddles the boundary; this proves it stays silent otherwise, so the fix isn't a table-scan running on every hot-path query. Two known-good regression queries were run against the real endpoint (search-service :5001), counting "Boundary tie detected" log lines immediately before and after. Both carry a `codiceMotore`, so they take the confirmed-car paths (`VectorSearchService.RankWithinSetAsync` for Type 3, graph Rule 10 for system search) and never reach `SymptomSearchService.FindBestMatchesAsync` where the re-query lives:

```
boundary-tie log lines BEFORE: 6

Q1  GET /api/search/symptom?q=Problemi iniezioni&codiceMotore=8140.43S&lang=it
    HTTP 200  →  resultType=document, count=4, documents=4          (expected 4 ✓)

Q2  GET /api/search/system?name=Iniezione&codiceMotore=F1AE0481D&lang=it
    HTTP 200  →  resultType=vague, count=14, documents=3, selectionNeeded=true
                 (Rule 10 multi-result: 14 total found, top 3 shown ✓)

boundary-tie log lines AFTER: 6
DELTA: 0  ✓
```

The re-query fired zero times across both normal queries, and both returned the expected result shapes. Combined with §21.4/21.5, this confirms the fix triggers **only** on a detected boundary tie, never on the general path.

## 22. H1 — JSON error handler added to chat-service & vehicle-service (2026-07-20)

Code-review finding H1: only search-service had a global `UseExceptionHandler` that turns an unhandled exception into a JSON body; chat-service and vehicle-service had none, so an unhandled exception on those two would surface ASP.NET's default response (an empty/HTML 500), which a fetch()-based frontend can't parse. Fix: copy search-service's exact handler block into both services' `Program.cs`, adapting only the log message and the user-facing string.

### 22.1 The change

Both files got `using Microsoft.AspNetCore.Diagnostics;` and, immediately after `var app = builder.Build();`, the same block search-service already uses:

```csharp
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    logger.LogError(feature?.Error, "Unhandled exception in <service>");
    context.Response.StatusCode = 503;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsync("{\"error\":\"<Service> temporarily unavailable\"}");
}));
```

| File | Message / body string |
|---|---|
| `services/chat/Program.cs` | "Unhandled exception in chat-service" / `{"error":"Chat service temporarily unavailable"}` |
| `services/vehicle/Program.cs` | "Unhandled exception in vehicle-service" / `{"error":"Vehicle service temporarily unavailable"}` |

Both containers were rebuilt (`DOCKER_BUILDKIT=0 docker compose build`) and recreated. The handler string is confirmed compiled into each running binary (`grep` inside the container's `/app/*.dll`).

### 22.2 vehicle-service — global handler demonstrated with a real unhandled exception

`VehicleSearchService.SearchAsync` has no try/catch around its Npgsql call, so stopping Postgres and hitting the endpoint forces a genuinely unhandled exception. Through **nginx (port 80)**, with Postgres stopped:

```
GET http://localhost/api/vehicles?marca=FIAT&modello=Ducato
→ {"error":"Vehicle service temporarily unavailable"}
   HTTP 503   Content-Type: application/json
```

Container log confirmed `Unhandled exception in vehicle-service` — i.e. the exception was genuinely unhandled and the global handler is what produced the JSON body. Before H1 this same trigger would have returned a non-JSON 500. This is the definitive proof the copied block works.

### 22.3 chat-service — identical block deployed; not deterministically triggerable via HTTP (and why that's expected)

The identical handler block is deployed and present in the running chat-service binary. But unlike vehicle-service, chat-service could not be made to throw an *unhandled* exception from outside, because every request path is already individually guarded:

- **Binding** — `[ApiController]` rejects malformed/missing inputs with its own 400 (e.g. `POST /api/chat/transcribe` with no `audio` field → framework 400 `application/problem+json`, before the action runs; the global handler is never reached).
- **`/api/chat/stream`** — `RepairOrchestrator` wraps the routing Gemini call and the tool/downstream HTTP calls in try/catch and yields a graceful `ServiceUnavailableResponse` on failure. Demonstrated live: with Postgres stopped, `POST /api/chat/stream` (a symptom + confirmed car, which routes to a search tool) returned `HTTP 200 text/event-stream` with `{"message":"Il servizio è temporaneamente non disponibile. Riprova a breve."}` — graceful degradation, not an unhandled exception.
- **transcribe / tts** — wrap their Gemini/Google calls in `try/catch` and return their own 503/502 JSON.

The only two unguarded paths that remain are the `RepairOrchestrator` **formatting** Gemini call (finding H2) and a `SynthesizeAsync` `JsonException`/`FormatException` on a malformed Google 200. Neither can be forced from outside deterministically: the formatting call shares the same Gemini key/endpoint as the routing call that must succeed first, and the TTS edge needs Google itself to return a 2xx with a malformed body. So chat-service's global handler is a genuine safety net for those residual paths, and its correctness is established by **code-identity** with the vehicle-service block proven live in §22.2 — not by a separate forced exception. (The robustness that prevents an easy trigger is itself the desirable property.)

### 22.4 Two incidental observations during the exercise

- **Finding H4 reproduced live.** Recreating the chat/vehicle containers gave them new IPs; the already-running nginx had cached the old ones and returned `HTTP 502` for every backend route until nginx was restarted. This is the documented "backend IPs cached at startup" nginx issue — confirmed real, not theoretical.
- **Docker Desktop engine crashed mid-exercise** (the `//./pipe/dockerDesktopLinuxEngine` named pipe disappeared, `com.docker.service` stopped) while Postgres was intentionally stopped for the vehicle demo. This was a host-level failure, unrelated to the code change. Docker Desktop was relaunched, the daemon recovered (server 29.0.1), and the full stack was brought back with `docker compose up -d`. The `./pgdata` bind mount persisted, so no data was lost.

### 22.5 Restore confirmation

Stack fully restored and healthy after recovery: Postgres healthy; normal traffic through nginx returns `HTTP 200` for vehicle (`/api/vehicles`), search (`/api/search/symptom` car path), and chat (`/api/chat/stream` returned `phase=chat, found=true, cases=4` for "Problemi iniezioni" + 8140.43S — matching §21.7's expected 4-doc result). The H1 `Program.cs` edits are intentionally **kept** (they are the fix, not scaffolding); only the forced-exception condition (stopped Postgres) was undone.

## 23. H2 — graceful fallback when the chat formatting Gemini call fails (2026-07-20)

Code-review finding H2: in `RepairOrchestrator.HandleMessageAsync`, the **second** Gemini call of a turn — the formatting/prose call — was not wrapped in try/catch, unlike the routing call above it. `GenerateAsync` throws `HttpRequestException` on any non-2xx (see `GeminiChatClient`). So a Gemini 429/500 on the formatting call — which happens *after* routing succeeded and the search already ran — propagated out through the SSE `await foreach` in `ChatController`, breaking the stream and taking the structured `cases` (`causa`/`intervento`, already retrieved verbatim from Search Service) down with it, even though those cases never depended on the formatting call.

### 23.1 The fix

Wrapped the formatting call in try/catch (`services/chat/Services/RepairOrchestrator.cs`). The `yield return BuildChatResponse(...)` stays **outside** the try (C# forbids `yield` inside try/catch, and it doesn't need to be inside — `rawResult` with the cases is already in hand). On failure the turn degrades instead of throwing:

- **Structured cases preserved** — `BuildChatResponse(rawResult, ...)` still emits them from `rawResult`; the document text is never touched and is **never** routed through any fallback LLM call, so the document-fidelity invariant holds.
- **`message` set to a fixed per-language fallback** — new `FormattingFallbackMessage(language)` helper, mirroring the existing `ServiceUnavailableResponse` per-language switch. Deliberately a "here are the results" line (`"Ecco i risultati trovati."` / `"Here are the results I found."` / …) **not** the service-unavailable string, which would misread when results are actually being shown. (Rationale for adding a new string rather than reusing: the task's first-choice was a "here are the results" line; none existed; the service-unavailable string reads wrong in this scenario.)
- **Logged** at `warn` level (`"Formatting call failed for session {SessionId}; …"`), like the boundary-tie log, for observability.
- **SSE completes normally with HTTP 200**; no unhandled exception escapes.

Routing determinism (§19) is a different call and was left untouched. Rule 1 (no document before car confirmation) is unaffected — the fallback only replaces prose; emission still runs through the same `BuildChatResponse` gate.

### 23.2 Forced-failure method

To force the formatting call to fail without waiting for a real Gemini outage, a **throwaway** mutation was added to `GeminiChatClient.GenerateAsync`: `if (operation == "formatting") throw new HttpRequestException(...)`. This is the most faithful simulation (the real non-2xx path throws exactly this type) and fails **only** the formatting call, leaving routing healthy. Built into the container, tested, then **reverted** (confirmed: `git diff` shows only `RepairOrchestrator.cs`; `GeminiChatClient.cs` byte-clean). Never committed.

### 23.3 Verification (live, through nginx port 80)

**(a) Graceful degradation — formatting call forced to fail**, query `Problemi iniezioni` + `codiceMotore=8140.43S`:

```
HTTP 200   Content-Type: text/event-stream
phase=chat  found=True  cases=4
message="Ecco i risultati trovati."      (Italian fallback, session language)
log:  warn: RepairOrchestrator[0] Formatting call failed for session h2-forced-fail; returning structured result with fallback message
```

- `cases=4`, and `causa`/`intervento` **byte-identical** to a normal run (compared keyed by `idDocumento`: 199309715/199309732/199309871/199310191 — identical). Document text was not altered.
- **No broken stream, no unhandled exception.**

**(b) H1 chat-handler gap — now closed live.** The formatting call is chat-service's one genuinely unguarded path from outside (§22.3 could only prove chat's H1 handler by code-identity). With the failure forced, the chat log shows the exception logged at **`warn`** by `RepairOrchestrator` (the H2 catch) immediately followed by the request completing (`info: ControllerActionInvoker[105]`) — and **no** `"Unhandled exception in chat-service"` (the H1 global handler's message), **no** `fail:`-level line. So **H2 catches the failure *before* it reaches the global handler** — the desired outcome. Chat's failure behaviour on its real unguarded path is now demonstrated live, not inferred.

**(c) Regression — normal path, mutation reverted, 3×** (same query):

```
run 1: HTTP 200 text/event-stream  phase=chat found=True cases=4
run 2: HTTP 200 text/event-stream  phase=chat found=True cases=4
run 3: HTTP 200 text/event-stream  phase=chat found=True cases=4
formatting-failure warnings this container: 0
```

`cases=4` stable across all three; `causa`/`intervento` byte-identical to baseline every run. `message` is **null** on the healthy path — correct, because `BuildFormatting`'s own rule returns null when a found-docs result "speaks for itself." That null-vs-fallback contrast is itself confirmation the catch fires *only* on failure: healthy → `null`; failed → `"Ecco i risultati trovati."`.

**(d) Clean tree / healthy stack.** `git diff` after revert shows only the intended `RepairOrchestrator.cs` H2 change; the throwaway mutation is gone; the full stack is up and serving 200s through nginx.

### 23.4 Files changed

| File | Change |
|---|---|
| `services/chat/Services/RepairOrchestrator.cs` | Formatting Gemini call wrapped in try/catch; on failure logs a warning, keeps the structured cases untouched, and substitutes the new `FormattingFallbackMessage(language)` per-language "here are the results" line. |

## 24. H4 (nginx per-request DNS) + H3/M7 (SSE streams incrementally, one bubble per turn) (2026-07-20)

Three code-review findings from the same area, committed as two units: H4 on its own (infra), then H3+M7 together (they fix one user-facing behaviour — incremental streaming — and are coupled: H3 makes the stream actually yield incrementally, which is exactly what arms M7's dormant duplicate-bubble bug).

### 24.1 H4 — nginx cached backend IPs → 502 after any container recreate

`nginx.conf` used static `proxy_pass http://<service>:<port>;`. nginx resolves those hostnames once at startup and caches the IPs, so a recreated backend (new IP) returned 502 until nginx itself was restarted — the 502-restart tax hit on every rebuild loop in prior sessions.

**Fix** (`nginx/nginx.conf`): `resolver 127.0.0.11 valid=10s;` (Docker's embedded DNS) + variable `proxy_pass $upstream_x$request_uri;` on every route. A variable in `proxy_pass` forces per-request re-resolution; `$request_uri` is appended explicitly because a variable `proxy_pass` no longer auto-forwards the URI (omitting it would silently route everything to `/`). The `$upstream_*`/`$request_uri` nginx vars survive the image's envsubst step (it only substitutes defined env vars like `${USAGE_DASHBOARD_KEY}` — the pre-existing `$http_x_usage_key` already relied on this).

**Verified live:** recreated all three backends; search-service and vehicle-service **swapped IPs** (.4↔.5). With nginx **not** restarted, every route returned the correct backend: `/api/vehicles`→34 cars, `/api/search`→`resultType=document count=4`, `/api/chat`→cases=4, `/api/usage`→403. No 502, no cross-wiring. The old static config would have 502'd or mis-routed. Committed as `fix(infra): nginx per-request DNS resolution … (H4)`.

### 24.2 H3 — nginx buffered the whole SSE stream

The chat-stream route had no `proxy_buffering off;` and `ChatController.Stream` set no `X-Accel-Buffering` header, so nginx buffered the entire `text/event-stream` body and released it at once — the intended "flush a fast confirmation event before the slow search" design never reached the browser incrementally.

**Fix** (both layers): `nginx/nginx.conf` chat route gets `proxy_buffering off; gzip off; proxy_read_timeout 300s;`; `ChatController.Stream` sets `Response.Headers["X-Accel-Buffering"] = "no"` (belt-and-suspenders — the header disables buffering for this specific response regardless of location config).

**Verified live at the transport layer, through nginx (port 80).** To get a two-event turn, a throwaway mutation gated on `sessionId.Contains("FORCEYIELD")` (so Gemini routing, which only sees the message, stayed clean and the real result still returned cases=4) yielded an early "Sto cercando..." event before the slow work. Per-event arrival times, timestamped from request start:

```
+0.254s   cases=0   message='Sto cercando...'
+4.189s   cases=4   message=''
gap first→last event: 3.934s
```

The first event landed **~3.9s before** the second while the stream was held open. If nginx were buffering, both would have arrived together at +4.2s; the early first event rules that out definitively. (Pre-fix, with neither `proxy_buffering off` nor `X-Accel-Buffering`, nginx's default `proxy_buffering on` batches the stream.) Confirmed chat-service emits `X-Accel-Buffering: no` on a direct `:5000` hit; the client sees it as absent through nginx because nginx consumes and strips that directive header — expected.

### 24.3 M7 — frontend appended a new bubble per SSE event

`ChatStore.send()`'s `onEvent` handler appended a **new** assistant message on every SSE event. Dormant while the backend single-yields (one event = one bubble), but the instant the stream yields twice (which H3 enables), the user gets duplicate stacked bubbles.

**Fix** (`frontend/src/app/services/chat-store.service.ts`): a turn = one `api.stream` call, so all its events share one assistant reply. The handler now generates one assistant-message id per turn: the first event **appends** the bubble; subsequent events **update it in place** by id (replacing text/carMatches/cases with the latest event, clearing any stale expansion). No backend turn-id was needed — the frontend already knows the turn boundary, so this is a pure frontend change.

**Verified — store reducer logic.** No browser automation is available in this environment (no host node/Playwright), so the reducer was proven directly: the exact append-vs-update-in-place branch, run in Node (via a `node:alpine` container) against two simulated events:

```
multi-yield turn : bubbles=1  cases-in-bubble=4  final-text=""
single-yield turn: bubbles=1  cases-in-bubble=4
PASS: exactly one bubble per turn; final results preserved
OLD code, multi-yield turn: bubbles=2   (the M7 bug, for contrast)
```

The new reducer collapses a two-event turn to **one** bubble carrying the final results; the old append-per-event code produced **two**. **Remaining manual check (stated honestly):** the reducer logic is proven, but the full Angular-signal + DOM render in a real browser was not exercised here — driving the live UI with a forced two-event turn and eyeballing a single rendered bubble is a remaining manual step. The backend half is already confirmed live (the FORCEYIELD capture in §24.2 shows the real stream now delivers two events).

### 24.4 Regression, fidelity, Rule 1

Throwaway mutation reverted (`git diff` clean of it; `RepairOrchestrator.cs` byte-matches its committed state), chat-service rebuilt clean, then a normal turn ("Problemi iniezioni" + `codiceMotore=8140.43S`) through nginx:

- **Single-yield:** exactly **1 event**, cases=4, HTTP 200, `text/event-stream`.
- **Fidelity:** `causa`/`intervento` **byte-identical** to the §23 baseline (keyed by idDocumento). Document text untouched — H3/M7 change only transport/rendering.
- **Rule 1:** structurally unaffected — H3/M7 don't touch `BuildChatResponse`'s car-confirmation emission gate; documents were emitted here only because a car was confirmed.
- **H4 bonus:** chat was reachable through nginx immediately after the chat-service recreate with no nginx restart — H4 working across a real recreate.

### 24.5 Files changed

| File | Change |
|---|---|
| `nginx/nginx.conf` | H4 (committed separately): resolver + variable `proxy_pass`. H3: `proxy_buffering off; gzip off; proxy_read_timeout 300s;` on the chat route. |
| `services/chat/Controllers/ChatController.cs` | H3: `X-Accel-Buffering: no` on the stream response. |
| `frontend/src/app/services/chat-store.service.ts` | M7: one assistant-message id per turn; first event appends, later events update in place instead of appending. |
