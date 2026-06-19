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
embeddings). **search**, **vehicle**, and **chat** still exist only as
compiling skeletons (see section 6). Build order from here follows the
roadmap: Vehicle → Search → Chat.

---

## 3. Current repo structure

```
semarepair_v2/                       (root folder name is lowercase on disk;
                                       doc's tree shows "SemaRepair-v2" —
                                       not renamed, flagged as a known gap)
├── services/
│   ├── chat/             — ASP.NET Core 8 skeleton, not implemented
│   ├── search/           — ASP.NET Core 8 skeleton, not implemented
│   ├── vehicle/           — ASP.NET Core 8 skeleton, not implemented
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
├── docker-compose.yml           — our-postgres + ingestion + ingestion-resx
├── docker-compose.override.yml  — local-dev-only port exposure
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

## 6. Search / Vehicle / Chat services — skeleton status

All three are ASP.NET Core 8 Web API projects, built and verified
(`dotnet build` succeeds, 0 errors/warnings) but contain **no real logic**
— every controller action and service method throws
`NotImplementedException`. This was an explicit scoping decision: lock in
the folder structure and API/data contracts now, implement logic later,
service by service, in roadmap order.

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

**search/** (`SearchController`, `GraphSearchService`,
`VectorSearchService`, `SymptomSearchService`, `ValidationService`):
- `SearchRequest`, `SearchResponse`, `DocumentResult`, `CarSummary`,
  `ValidationResult` models are **real**, not stubs — their shapes are
  copied directly from the JSON contract and the `ValidateSymptom` C# code
  already fully specified in the architecture doc (section 6.4, 9, 5.10
  Rule 12).
- Connects to `OUR_DB` (env var, same convention as ingestion).
- `/health` implemented per section 9.7's contract.
- The data this service needs (`graph_edges`, `document_embeddings`,
  `symptom_embeddings`) **now actually exists and is populated** —
  unblocked since the last update.

**vehicle/** (`VehicleController`, `VehicleSearchService`):
- `VehicleQuery`, `VehicleResponse`, `VehicleResult` models match section
  6.5's contract.
- **Important caveat:** the architecture doc says Vehicle Service should
  query *Their SQL Server*. We don't have that access yet, so it's wired
  to `OUR_DB` (our local Postgres stand-in / `gup_rows`) for now, with a
  comment flagging the swap-over point.

**chat/** (`ChatController`, `RepairOrchestrator`):
- `ChatRequest`/`ChatResponse` models are a **first draft** — unlike
  search/vehicle, the architecture doc never gives Chat a formal JSON
  contract, only a narrative example (section 8 of the technical doc:
  `phase`, `found`, `cases`). Expect this to be revised once Chat is
  actually built (it's last in the roadmap order, and depends on
  Search + Vehicle existing first).
- No DB access — it's meant to call Search/Vehicle services over HTTP
  (`HttpClient` registered via `AddHttpClient<RepairOrchestrator>()`).

None of the three are wired into `docker-compose.yml` yet (no point
containerizing services with no logic). That's the natural next step once
one of them gets implemented for real.

---

## 7. Infrastructure / docker-compose

`docker-compose.yml` now has four services: `our-postgres`
(pgvector/pgvector:pg16), `ingestion` (xlsx, depends on postgres healthy),
`ingestion-resx` (resx/graph/embeddings, depends on postgres healthy AND
`ingestion` completing successfully via
`condition: service_completed_successfully`).

`docker-compose.override.yml`: auto-merged by `docker compose up` (no
flag needed) — adds `our-postgres` port 5432→host, for connecting from
host tools (psql/DBeaver) only. Containers talk to Postgres over the
internal Docker network as `our-postgres:5432`, not via this mapping.

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
  referenced by docker-compose — it would fail to resolve `chat-service`/
  `search-service`/`vehicle-service`/`frontend` hostnames since those
  containers don't exist yet.
- No `.gitignore` exists yet (project isn't a git repo currently) —
  `pgdata/`, `services/*/bin|obj`, and `.env` should be excluded if/when
  one is added (the real Gemini key currently lives in plain `.env`).
- No automated tests anywhere in the project yet.
- `FaultCode.description` text is parsed from resx but has nowhere to live
  in the current schema (see section 5.1) — will need a decision (new
  table? property on an existing one?) before Search/Chat can surface it.
- The doc's section 5.5 lists 11 graph relationship types in its
  conceptual diagram; only 7 are ever specified as buildable (section
  5.8) and that's all we built. The other 4 (Brand→Model, Model→Car,
  Document→HAS_SYMPTOM→Symptom, System→CONTAINS→Device) are not
  implemented — flagging in case a future session assumes they exist.

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
- **Search/Vehicle/Chat skeletons:** intentionally incomplete — every
  `NotImplementedException` is a known gap, not an oversight. `Npgsql`
  package is referenced in `search`/`vehicle` `.csproj` files but unused
  by any actual code yet. No `.sln` file ties the three projects together.
- **No secrets at risk in shared text, but real secret now exists
  on disk:** `.env` contains a real Gemini API key as of this update —
  previously it was only dummy Postgres credentials. If this project is
  ever put under git, `.env` must be excluded from the very first commit,
  not added later.

---

## 11. Suggested next step

Ingestion (both halves) is now fully done and verified — xlsx, resx
parsing, the graph, and real embeddings all exist and are idempotent.
Per the roadmap order, the natural next step is implementing **Vehicle
Service** for real (simplest — pure SQL filtering over `gup_rows`, no
embeddings/graph needed), then wiring it into `docker-compose.yml`.
**Search Service** is also now actually unblocked (its data dependencies
all exist), so it could reasonably go next instead if that's preferred
over strict roadmap order.
