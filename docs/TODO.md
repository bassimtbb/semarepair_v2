# SemaRepair v2 — TODO

---

## Section 1: Fixes & Verifications

Items that are broken, incomplete, or only partially verified. A mechanic
using the app today could be misled or hit a dead end because of these.

---

### Voice Mode

- [ ] **Rule 8 cross-brand path never live-tested — Phase 1 gate not yet passed**
  - Where: `frontend/src/app/services/voice-mode.service.ts` `buildSpokenText()`
  - Why it matters: the Phase 1→Phase 2 gate requires the Rule 8 two-turn ask-first flow to be verified live before any Phase 2 file is created; the fix (flag + document-absence guard) is code-complete and root-cause-confirmed from code reading, but was not verified in a browser session because Docker went down during the test
  - What's needed: start Docker; confirm CITROEN Jumper 4HV (CI2505) in a session; find a fuel/injection fault code present in FI2515 (FIAT Ducato 4HV) but absent in CI2505's 1 document — e.g. a B- or P-prefix code from FI2515's 10 docs with system in `SystemCategoryLookup.AllowsEngineFallback`; send it; verify Turn 1 speaks only the disclosure; say "sì"; verify Turn 2 speaks causa+intervento (not the disclosure again); confirm `console.debug` shows `foundViaSharedEngine=true` on both turns

---

### Chat Service

- [ ] **Real browser mic recording never tested against Gemini** (see §6.4, §9)
  - Where: `services/chat/Services/GeminiChatClient.cs` `TranscribeAsync`; `frontend/src/app/components/chat/chat-input/chat-input.component.ts`
  - Why it matters: every mechanic using the mic button in a real browser produces `audio/webm` (standard `MediaRecorder` output), which is not in Gemini's documented supported format list — verified only with a synthetic WAV file; could silently fail or garble every real voice input
  - What's needed: verification — Playwright with `--use-fake-device-for-media-stream --use-fake-ui-for-media-stream` flags to capture real `audio/webm` and confirm Gemini transcribes it correctly; code fix (MIME detection + reject with clear error, or pass-through if Gemini is permissive) if it fails

- [ ] **`SearchBySystem` tool path never exercised through Chat in a real browser session** (see §6.4, §9)
  - Where: `services/chat/Services/ToolDefinitions.cs`, `services/chat/Services/RepairOrchestrator.cs`
  - Why it matters: the tool exists and the search endpoint is verified via direct curl, but if the routing prompt fails to invoke it correctly in practice, a mechanic asking about a system (e.g. "problemi ai freni") silently gets a not-found or falls back to a wrong tool
  - What's needed: verification — real chat session with a system-keyword symptom, inspect the actual Gemini tool call name and args against search-service's real response

- [x] **Rule 10's 5+ document branch unverified for fault-code and system search** (see §6.1)
  - Where: `services/search/Controllers/SearchController.cs`; `services/chat/Services/SystemPromptBuilder.cs` `BuildFormatting`
  - Why it matters: the architecture doc specifies distinct response behavior when 5+ graph matches are found; the 1-doc and 2-4-doc paths have been exercised with real data, but no test has triggered the 5+ path — if it is not implemented, a fault code with many documents produces an undefined response
  - What's needed: verification — find or construct a fault code with 5+ linked documents in `graph_edges`; confirm the response shape and that `BuildFormatting`'s formatting call handles it coherently

- [ ] **`BuildFormatting` prompt never audited end-to-end after all rule additions** (see §6.19–6.22)
  - Where: `services/chat/Services/SystemPromptBuilder.cs` `BuildFormatting`
  - Why it matters: Rules 8b/8c/8d/9b were each added independently; no single test has covered all four new rule branches, and two specific intersections are most likely to produce conflicting instructions:
    - **Low-confidence match AND secondary symptom in the same turn**: if the primary search returns `lowConfidenceMatch: true` AND a `secondarySymptom` was provided, the prompt receives both Rule 8b (disclosure + question about the low-confidence doc) and Rule 8d/9b (secondary symptom result/not-found) simultaneously — which takes precedence, and does the output make sense to a mechanic?
    - **Secondary symptom retry on a shared-engine result**: if the secondary symptom auto-retry (Rule 8d) produces a result via the `SHARES_ENGINE_WITH` fallback, the response carries both `foundViaSharedEngine: true` and the secondary-symptom framing — the prompt has both Rule 8a (shared-engine transparency) and Rule 8d (secondary symptom) active at once, which was never tested
  - What's needed: manual read-through of `BuildFormatting` in full to check whether these intersections are handled or silently skip a rule; then a targeted live test for each combination above

- [x] **Reset endpoint (Rule 6) never confirmed end-to-end through the real frontend** (see §6.16)
  - Where: `services/chat/Controllers/ChatController.cs`; `frontend/src/app/services/chat-store.service.ts` `reset()`
  - Why it matters: the × badge clears the UI client-side, but whether `chat.reset()` also calls the backend `/api/chat/reset` (if it exists) to clear server-side session history was never verified — a mechanic who resets and starts over may still have a ghost history on the backend influencing Gemini's routing
  - What's needed: verification — check whether `ChatController` exposes a reset/delete endpoint and confirm the frontend calls it; if it doesn't, the backend session is only cleared on timeout (or restart), not on demand

---

### Search Service

- [x] **`QueryEmbedder.EmbedAsync` has no error handling — raw 500 on any Gemini failure** (see §6.1 audit)
  - Where: `services/search/Services/QueryEmbedder.cs` lines 43–48
  - Why it matters: `EnsureSuccessStatusCode()` throws `HttpRequestException` on any Gemini 4xx/5xx/timeout; `ReadFromJsonAsync` throws on malformed response — neither this method nor its callers catch these; a Gemini rate-limit or outage returns an unhandled 500 to the frontend with no user-facing message, unlike `chat-service` which has per-turn try/catch at every Gemini call site
  - What's needed: code fix — try/catch in `EmbedAsync` returning a graceful `503`/`500` with a "search temporarily unavailable" message, or a global exception-handling middleware in `search-service/Program.cs`

---

### Vehicle Service

- [ ] **`VehicleSearchService.SearchAsync` has no LIMIT or pagination** (see §6.2 audit)
  - Where: `services/vehicle/Services/VehicleSearchService.cs` `SearchAsync`
  - Why it matters: returns every matching row unbounded; the frontend already groups 32-car results with a filter UI, but at production scale (thousands of real vehicles) this is an unbounded query on the hot path — the frontend groups all results in memory, which also becomes a problem
  - What's needed: code fix — add `LIMIT N` (e.g. 200) to the query + a `TotalCount` field in `VehicleResponse` so the frontend can surface "showing first N of M"

- [x] **`VehicleController.GetById` returns 204 (No Content) for missing IDs — should be 404** (see §6.2 commit)
  - Where: `services/vehicle/Controllers/VehicleController.cs`
  - Why it matters: 204 means "request succeeded, nothing to return" — callers expecting REST convention for a missing resource (including `RepairOrchestrator.ConfirmCarAsync`) may not correctly handle 204 as "this car ID does not exist"
  - What's needed: code fix — return `Results.NotFound()` instead of `Results.NoContent()`

---

### Infrastructure

- [x] **Swagger UI unconditionally enabled in all 3 C# services — no dev-environment gate** (see §6.1/6.2/6.4 audit)
  - Where: `services/chat/Program.cs:17–18`, `services/search/Program.cs:21–22`, `services/vehicle/Program.cs:12–13`
  - Why it matters: exposes full internal API schemas + try-it-out in any deployment environment, not just local dev
  - What's needed: code fix — wrap `app.UseSwagger(); app.UseSwaggerUI();` in `if (app.Environment.IsDevelopment())` in all 3 files; three-line change each, zero behavior change in dev

---

### Frontend

- [x] **car-card shows "2021–9999" while header badge shows "2021–oggi" — same data, two different representations** (see §6.15)
  - Where: `frontend/src/app/components/cards/car-card/car-card.component.ts`
  - Why it matters: a mechanic sees the car-selection list showing "9999" as a literal year, then clicks through to a confirmed-car badge that shows "oggi" — visually inconsistent on the same screen for the same car
  - What's needed: code fix — one-line template change to render `annoFine === 9999` as "oggi" (or per-language equivalent already used in `RepairOrchestrator.BuildVehicleNotFoundMessage`) in the car-card year-range span

- [ ] **Muted text on `--color-card-surface` fails WCAG AA — measured 3.707:1, floor is 4.5:1** (see §6.7)
  - Where: `frontend/src/styles.css` (`--color-card-surface` token); `frontend/src/app/components/cards/car-card/car-card.component.css` muted detail text
  - Why it matters: this is a measured failure against a real standard, not a subjective "could be better" — the pre-existing baseline was already below AA (4.230:1); this change moved it further down; the explicit acceptance was "smallest available regression," not "acceptable to ship"
  - What's needed: code fix — either lighten `--color-card-surface` to widen the contrast margin for muted text, or introduce a separate `--color-muted-on-card` token that's darker than `--color-muted` specifically for this surface; re-measure all contrast pairs that depend on these tokens after any change

---

### Ingestion

- [ ] **Fault-code description text is parsed from resx then silently discarded — data loss** (see §5.1, §6.14 context)
  - Where: `services/ingestion-resx/graph_builder.py`; `services/ingestion-resx/schema.sql` `graph_edges` table
  - Why it matters: `resx_parser.py` extracts per-code descriptions (e.g. "P0504 — Relazione interruttore freno 1-2 - Rapporto errato") into a `fault_code_descriptions` dict on every parsed record; `graph_builder.py` and `seeder.py` never write them anywhere — the text is computed and immediately discarded; a mechanic sees bare code badges with no explanation of what each code means
  - What's needed: schema change + ingestion fix — add a `description TEXT` column to `graph_edges` (with `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` since the table already has data), populate it on `CONTAINS_FAULT` edge rows from the parsed dict, then thread the field through `DocumentContentService` → `SearchResponse` → `RepairOrchestrator` → `CaseSummary` → `DtcBadge` in the frontend; requires a `TRUNCATE graph_edges` and re-seed to backfill

- [ ] **`generate_embeddings()` commits once at end of loop — crash mid-run loses the whole batch** (see §10)
  - Where: `services/ingestion-resx/embedder.py`
  - Why it matters: acceptable at 540 records; at 20k–60k production records, a crash partway through (network, OOM, Postgres error) discards all inserts for that run; the per-record try/except only protects against that record's API call failing, not a DB-level failure
  - What's needed: code fix — chunked commits (e.g., every 50 records) inside the embedding loop, same pattern as the per-record exception isolation already in place

---

### Testing

- [ ] **Zero automated tests exist anywhere in the project** (see §9, §10)
  - Where: all services (`services/chat/`, `services/search/`, `services/vehicle/`) + frontend
  - Why it matters: every verified behavior was confirmed by hand with no regression protection — any code change to `RepairOrchestrator`, `GraphSearchService`, or the frontend store could silently break a verified scenario with no failing test to catch it
  - What's needed: at minimum, integration tests for search/vehicle endpoints using test fixtures; frontend unit tests for `ChatStore`'s state transitions (car confirmation, language detection, secondary symptom retry)

---

### Design Decisions Requiring Resolution

- [ ] **Scenario 8 (Type 3 symptom search — SHARES_ENGINE_WITH fallback) is dead code on local data** (see §6.21)
  - Where: `services/search/Controllers/SearchController.cs` `SymptomWithCarAsync` line 121
  - Why it matters: the architecture doc specifies this fallback, but the trigger condition (`candidateDocs.Count == 0` — zero documents at all for the confirmed car) is never true on the local 108-document sample; the structurally analogous Type 1/2 fault-code/system fallback uses a looser trigger ("no match for this specific fault code/system") and has been verified live with real data
  - What's needed: design decision — either document explicitly that Type 3's trigger is intentionally stricter than Type 1/2 (and accept that this path is only reachable at production scale), or align the trigger with Type 1/2's "no good match for this symptom" criterion; cannot be verified until real production data is available

---

## Section 2: Enhancements

Things that work but could be more reliable, observable, or maintainable. Nothing here fixes a mechanic-facing correctness gap, but each would improve production readiness.

---

### Resilience

- [ ] **Gemini retry/backoff not implemented in Chat Service** (see §9, arch doc §9.1)
  - Current behavior: first Gemini failure → immediate apology message, no retry
  - Improved behavior: 3 retries with 1s/2s/4s backoff before falling back to the apology; search-service's Python ingestion already has this pattern; arch doc §9.1 specifies it explicitly
  - Effort: small

- [ ] **nginx static `proxy_pass` caches backend IPs at startup — any backend container restart causes 502 until nginx is also force-recreated** (see §7)
  - Current behavior: `proxy_pass http://search-service:5001` resolves the hostname once at nginx startup; if `search-service` restarts (new IP), nginx keeps routing to the stale IP — manifested as a real live 502 during development after `docker compose up --build`
  - Improved behavior: use Docker's embedded DNS + a Lua/variable proxy_pass pattern (e.g. `resolver 127.0.0.11 valid=10s; set $backend http://search-service:5001; proxy_pass $backend;`) to resolve per-request, not at startup
  - Effort: small

- [ ] **Chat session state in-memory only — lost on restart, won't scale beyond one instance** (see §9, §6.4)
  - Current behavior: `ConcurrentDictionary` in `SessionStore.cs`; a container restart silently drops every active mechanic's session history and confirmed car
  - Improved behavior: Redis or Postgres-backed session store; arch doc §13 explicitly flags this as an unresolved question pending the company meeting's auth/hosting decisions
  - Effort: medium (blocked on session-retention and auth decisions from the company meeting — flag for scheduling after §3 decisions are made)

- [ ] **ingestion-resx has no retry around the initial Postgres connection** (see §10)
  - Current behavior: a Postgres connection failure at script startup aborts with a Python traceback, no retry
  - Improved behavior: 3–5 retries with backoff on `psycopg2.connect()` at startup, consistent with the per-record embedding retry already in `embedder.py`; only relevant before any production cron/scheduled use
  - Effort: small

---

### Observability

- [ ] **`gemini_usage_log` has no retention or pruning policy** (see §6.17)
  - Current behavior: the table grows indefinitely; no TTL, archive, or row-count cap
  - Improved behavior: a scheduled `DELETE WHERE logged_at < NOW() - INTERVAL '90 days'` (or similar) run as a Postgres cron job or a docker-compose scheduled task; at current prototype API-call rates this is negligible, but at production scale with many simultaneous mechanics the table will grow large quickly
  - Effort: small

---

### Dead Code

- [ ] **`SearchRequest` class is never bound by `SearchController` — a false affordance that will cause a real bug** (see §8 decisions log)
  - Current behavior: all three `SearchController` endpoints take individual `[FromQuery]` parameters directly; `SearchRequest` has no live effect on any request — but a future developer reading this class will naturally assume it is the actual request model, add fields to it, and discover nothing changed (or worse, wire it as a `[FromQuery]` parameter type thinking they're fixing a missed binding, which would silently swap the live contract to a new shape)
  - Improved behavior: either delete the class entirely (and update any doc references) or immediately wire it as the real `[FromQuery]` parameter type and remove the individual parameters — leaving it as-is guarantees a future misread
  - Effort: small

- [ ] **`SessionStore.ConfirmCar()` is never called — dead method** (see §8 decisions log)
  - Current behavior: `RepairOrchestrator` manages `Session` fields directly; `ConfirmCar()` has no callers in the codebase
  - Improved behavior: delete the method, or add a comment explaining that session state is managed inline by design (so a future reader doesn't add callers to restore "missing" functionality)
  - Effort: small

---

### Non-Italian FindCar Reliability

- [ ] **FindCar structured fields not stress-tested for French and Portuguese** (see §9)
  - Current behavior: Italian/English FindCar: 5/5 confirmed; Spanish fuel translation was fixed (§8); French and Portuguese structured field extraction (fuel, motorizzazione, year) have not been systematically tested against real `gup_rows` data
  - Improved behavior: a targeted test matrix (each of the 5 languages × each structured Gemini-extracted field: `fuel`, `motorizzazione`, `year`) using the same approach that found the two existing bugs — temporarily log Gemini's actual tool-call args and compare against what `gup_rows` stores
  - Effort: small (test work; may reveal code fixes in `SystemPromptBuilder.BuildRouting`)

---

### Admin / Operations

- [ ] **No force-reingest flag — re-seeding requires a manual `TRUNCATE`** (see §5.4, arch doc §8.4)
  - Current behavior: `ingestion-resx` is idempotent; running when tables are non-empty is a no-op; a full re-seed requires manually `TRUNCATE`-ing `graph_edges`, `document_embeddings`, `symptom_embeddings` before re-running
  - Improved behavior: a `--force` flag or `FORCE_REINGEST=true` env var that truncates the relevant tables and re-runs; arch doc §8.4 describes this as "Trigger 3 — Manual admin endpoint"
  - Effort: small

---

### Security / Infrastructure

- [ ] **nginx rate limiting not implemented** (see arch doc)
  - Current behavior: no rate limiting on any nginx location; any client can send unlimited requests
  - Improved behavior: `limit_req_zone` + `limit_req` per the arch doc's rules — per-IP with a burst allowance large enough not to penalize the car-selection confirm-then-replay pattern (which fires two requests in quick succession)
  - Effort: small

- [ ] **SSL not implemented — all traffic is plain HTTP** (see §9, arch doc)
  - Current behavior: nginx listens on port 80 only; no TLS anywhere
  - Improved behavior: TLS termination at nginx; blocked on the hosting target decision from the company meeting (see §3)
  - Effort: medium (once hosting is decided)

---

### Design / Architecture

- [ ] **SHARES_ENGINE_WITH build-time brand filter — confirm at company meeting** (see §5.2)
  - Current behavior: local decision: any two distinct cars sharing an engine code get linked, regardless of brand (both directions, for symmetric traversal); arch doc's "cross-brand transparency" framing at search-time implies only cross-brand links matter, but the doc's own build pseudocode doesn't filter by brand
  - Improved behavior: confirm with the company whether same-brand engine-sharing links should be included or excluded; the decision affects Rule 8's "found via shared engine" messaging
  - Effort: zero code until confirmed; one SQL filter if same-brand should be excluded

- [ ] **4 unbuilt graph relationship types not documented as intentionally excluded** (see §9, arch doc §5.5)
  - Current behavior: arch doc §5.5's conceptual diagram shows 11 relationship types; only 7 are specified in §5.8 and built; Brand→Model, Model→Car, Document→HAS_SYMPTOM→Symptom, System→CONTAINS→Device are absent with no in-code explanation
  - Improved behavior: add a comment to `services/ingestion-resx/schema.sql` noting these 4 types are part of the conceptual model but not implemented, and that no current search path traverses them — prevents a future session treating their absence as a bug to fix
  - Effort: trivial (comment-only)

---

## Section 3: Blocked on Company Meeting

Not actionable locally. These require real company input, data access, or a product decision before any local work is meaningful.

- **Total real document count** (5k conservative / 20k realistic / 60k full platform) — directly affects hosting size, embedding API cost, HNSW index parameters, and ingestion runtime estimates
- **Complete vehicle brand list** — local prototype covers only FIAT/FORD/CITROËN/PEUGEOT/IVECO; the full list determines whether `SystemCategoryLookup`'s 8-entry allow-list needs expansion and whether FindCar's routing examples cover the real brand vocabulary
- **Data access method** — direct SQL Server read-only access vs. bulk resx export vs. webhook-push; gates the production ingestion pipeline entirely; Vehicle Service's `VehicleSearchService.cs` already has the swap-over point commented
- **Auth model** — who accesses the chat UI, how mechanics are authenticated (none / API key per-mechanic / JWT / SSO); also determines whether the shared-secret usage dashboard gate is acceptable long-term
- **Conversation history retention** — whether to store session history at all; if yes, where (Postgres / Redis) and for how long; unblocks the in-memory session storage enhancement above
- **Hosting target** — VPS / cloud / company server; unblocks SSL cert strategy, load-balancer config, multi-instance Chat Service planning
- **Document update mechanism** — nightly cron re-ingest vs. webhook push for newly published repair documents; affects whether the force-reingest admin trigger (§2 above) needs to be a safe no-downtime operation
- **Image handling** — resx files reference `GETFILE:{id}` image IDs; whether those images are accessible via the company's system, and whether to include them in the knowledge base, has no answer yet
- **Redis decision** — arch doc §13 flags Redis as a candidate for session persistence; unresolved alongside the conversation-history-retention decision above
- **SHARES_ENGINE_WITH cross-brand filter confirmation** — local decision (any two distinct cars sharing an engine code) was made to match the arch doc's literal pseudocode; company may want only cross-brand links to match the "cross-brand transparency" framing in doc §5.6 Rule 8
- **FI0427/FI0429 duplicate vehicle records** — confirm with the data team whether these are genuinely distinct vehicles or data entry errors before assuming production data is clean
- **Repair-case card field label localization** — `Impianto`, `Dispositivo`, `Anomalia`, `Causa`, `Intervento`, `Nota`, and the two section headings ("Identificazione del sistema / guasto", "Procedura di riparazione") are hardcoded Italian in `repair-case-card.component.ts` regardless of session language; explicitly flagged in §6.12 as a known convention to revisit; depends on a company decision about which languages require full UI chrome localization in production (not just document content, which already varies per language)
