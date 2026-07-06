# Gemini Usage & Cost Dashboard — Architecture Plan

Native implementation using the existing stack only: ASP.NET Core 8 (chat-service,
search-service), Python (ingestion-resx), shared Postgres (`OUR_DB`), nginx, Angular 19.
No new external services, SDKs, or LLMOps platforms.

## Quick explainer: no separate "dashboard service", and where token counts/cost actually come from

**Why there's no dedicated dashboard/usage microservice**: this was a deliberate
choice, not an oversight. A dashboard needs two very different things - *writing*
usage rows (done by whichever service actually calls Gemini) and *reading*
aggregates (done once, centrally). Spinning up a fourth/fifth backend service just to
host 3 read-only SQL queries would be more new infrastructure than the feature
itself - a new Dockerfile, a new nginx route, a new deploy target, for code that's a
few hundred lines of raw SQL. Instead:
- **Writes** happen inside the 3 existing places that already call Gemini -
  `GeminiChatClient` (chat-service), `QueryEmbedder` (search-service), `embedder.py`
  (ingestion-resx). Each writes only its own rows.
- **Reads** (the 3 `/api/usage/*` endpoints the dashboard calls) were added to
  **search-service** specifically - not a new service - because search-service
  already had everything this needed: a live `OUR_DB` Postgres connection, and an
  existing raw-SQL aggregation style (`GraphSearchService`) to extend. The new
  `UsageController`/`UsageQueryService` in `services/search/` are the entire
  "backend for the dashboard" - there's no separate process for it.

So the actual topology is: 3 writers (chat-service, search-service, ingestion-resx)
all writing into one shared table, 1 reader (search-service, the same service, just a
different controller) that the Angular dashboard talks to over `/api/usage/*`. No new
service was created or needed.

**How a token count is actually obtained** - this is the single most important fact
in this whole feature, and it's a real Gemini API limitation, not a choice this
build made:

- Gemini's `generateContent` endpoint (used only by chat-service, for every actual
  chat reply) **always returns a real, measured `usageMetadata` object** in its HTTP
  response: `promptTokenCount`, `candidatesTokenCount`, `thoughtsTokenCount`,
  `totalTokenCount`. `GeminiChatClient.cs` deserializes this directly from Gemini's
  own response - nothing is computed or guessed. These rows are written with
  `is_estimated = false`.
- Gemini's `embedContent` endpoint (used by search-service's `QueryEmbedder` and
  Python's `embedder.py` - never by chat-service) **does not return any token usage
  at all**, confirmed by inspecting its actual response shape (it only ever contains
  `embedding.values`). For these two call sites, there is no real number to read -
  so a token count is *estimated* from the input text's character length
  (`characters_per_token` in `config/gemini-pricing.json`, currently 4). These rows
  are written with `is_estimated = true`, and the dashboard always shows that flag
  (a "~stima" badge) rather than presenting a guess as if it were a fact.

**How the dollar cost is generated** - computed once, at the moment each call is
logged, never in the frontend and never recomputed later:
1. `config/gemini-pricing.json` holds a price-per-million-tokens for each model
   (input and output rates separately for `gemini-2.5-flash`; a single rate for
   `gemini-embedding-001`, since embeddings have no "output"). This file is read
   once at service startup and cached - it's the one place a price lives, shared by
   both the C# and Python code (each has its own small `GeminiPricing`-equivalent
   class/function that reads the same file, since there's no shared library between
   the services).
2. For a chat-service call: `cost = (promptTokens / 1,000,000 * inputPrice) +
   (completionTokens / 1,000,000 * outputPrice)`, where `completionTokens` is
   `candidatesTokenCount + thoughtsTokenCount` added together - a real bug was found
   and fixed during the build where Gemini 2.5 Flash's hidden "thinking" tokens
   weren't being counted, which would have silently undercounted real cost on every
   single call (see section 0/7 below for how that was caught).
3. For an embedding call: `cost = estimatedTokens / 1,000,000 * embeddingPrice`.
4. If the model isn't in the pricing file at all, `cost_usd` is stored as `NULL`,
   never a guessed/default price - a missing price should be obviously visible as
   missing, not silently wrong.

That computed `cost_usd` (plus the token counts, `is_estimated`, and the verbatim raw
API response) is what actually gets written to the `gemini_usage_log` row. Everything
the dashboard displays - the KPI cards, the chart, the log table - is just SQL
aggregates (`SUM`/`COUNT`/`GROUP BY`/`date_trunc`) over that one table, computed by
search-service's `UsageQueryService` and returned as-is. No number is ever computed
twice or massaged on the way to the screen.

## 0. What's actually true about the current code (read before building)

Three places call Gemini today, each with a single chokepoint method — that chokepoint
is where interception belongs, not the call sites that use it:

| Service | File | Chokepoint | Gemini endpoint | Model (today, hardcoded) |
|---|---|---|---|---|
| chat-service | `Services/GeminiChatClient.cs` | `GenerateAsync(...)` (also used internally by `TranscribeAsync`) | `generateContent` | `gemini-2.5-flash` |
| search-service | `Services/QueryEmbedder.cs` | `EmbedAsync(string text)` | `embedContent` | `gemini-embedding-001` |
| ingestion-resx | `embedder.py` | `Embedder.embed(text, task_type)` | `embedContent` (via `google-genai`'s `models.embed_content`) | `gemini-embedding-001` |

Every Gemini call in the whole system passes through exactly one of these three
methods. `RepairOrchestrator.cs` calls `GenerateAsync` twice per turn (routing call,
formatting call) but never bypasses it. This is the single most useful fact for this
feature: **instrument 3 methods, not N call sites.**

**Critical asymmetry that drives the whole design**: Gemini's `generateContent`
response always includes a top-level `usageMetadata` object
(`promptTokenCount`/`candidatesTokenCount`/`totalTokenCount`). Gemini's `embedContent`
response does **not** — it only ever returns `embedding.values`. This is a real API
limitation, not a gap in this codebase. It means:
- chat-service can log **exact, measured** token counts for every call.
- search-service and ingestion-resx can only ever log an **estimated** token count
  (derived from input text length), never a measured one, for as long as they call
  `embedContent`.

This must be visible in the data, not hidden — add an `is_estimated` flag per row.
Silently presenting an estimate as a fact would break the same fidelity principle this
project already applies everywhere else (never present an invented number as if it
were a real one).

Other facts that shape the plan:
- chat-service today has **no Postgres connection at all** (only `GEMINI_API_KEY`,
  `SEARCH_SERVICE_URL`, `VEHICLE_SERVICE_URL` in `docker-compose.yml`). Adding logging
  means giving it `OUR_DB` for the first time.
- search-service and ingestion-resx already connect to `OUR_DB` (Npgsql / psycopg2),
  raw SQL throughout, no ORM, no EF Core — stay consistent with that.
- Schema today lives in `services/ingestion-resx/schema.sql`, applied once at startup
  by `seeder.py`. Neither chat-service nor search-service have their own
  schema/migration mechanism.
- The frontend has **no routing configured at all** today (`app.config.ts` has no
  `provideRouter`, no `app.routes.ts` exists) — it's a single root component. Adding a
  dashboard page is the natural moment to introduce routing, not optional.
- No charting library exists in `package.json`.
- nginx routes `/api/chat`, `/api/search`, `/api/vehicles` by regex prefix to their
  respective services, with `/` falling through to the frontend container.

## 1. Database: one shared table, one shared pricing file

### 1.1 `gemini_usage_log` table

One row per Gemini call, written by whichever service made the call. Lives in the
same `OUR_DB` Postgres instance everything else already uses — no new database.

Columns:
- `id` — bigserial primary key.
- `occurred_at` — timestamptz, default now().
- `service_name` — text (`chat-service` | `search-service` | `ingestion-resx`).
- `operation` — text, nullable. The specific call site: `routing`, `formatting`,
  `transcription` (chat-service); `query_embed` (search-service); `document_embed`,
  `symptom_embed` (ingestion-resx). Cheap to add now, valuable later — lets you answer
  "which call type is actually expensive" instead of just "which service."
- `model` — text (`gemini-2.5-flash`, `gemini-embedding-001`).
- `prompt_tokens`, `completion_tokens`, `total_tokens` — integer, nullable.
  `completion_tokens` is always null for embedding calls (there's no "completion").
- `is_estimated` — boolean. True for every `embedContent`-derived row, per the
  asymmetry above. Always false for chat-service rows.
- `cost_usd` — numeric, computed at write time (see 1.2). Never recomputed in place
  later — if pricing changes, new rows use the new price, old rows keep what was true
  when they were written. This matches how a real invoice works.
- `session_id` — text, nullable. For chat-service rows, this is the same `sessionId`
  `ChatStore` already generates per browser tab and sends on every request. Free,
  high-value correlation: "how much did this one mechanic conversation cost."
- `ingestion_run_id` — integer, nullable, references `ingestion_log.id`. For
  ingestion-resx rows. `ingestion_log` already exists in `schema.sql`
  (`documents_processed`, `embeddings_generated`, `edges_created`, `duration_seconds`,
  ...) — reuse it instead of inventing a parallel "run" concept. One ingestion run's
  total embedding cost becomes a join, not new infrastructure.
- `raw_usage_json` — jsonb, nullable. The verbatim `usageMetadata` object (or, for
  embedding calls, the verbatim request text length / whatever the estimate was
  derived from). Never throw the raw fact away — if the price table or the estimation
  method turns out to be wrong, this is what lets you recompute historical cost
  correctly instead of having to guess.

Indexes: `occurred_at`, `service_name`, `model` — exactly the three things the
dashboard will filter and group by.

**Where the `CREATE TABLE IF NOT EXISTS` lives**: add it directly to
`services/ingestion-resx/schema.sql`, the one place "schema" already means something
in this codebase, applied automatically on every `docker-compose up` via the existing
`seeder.py` mechanism. Don't invent a second migration system (e.g. EF Core
migrations, or a bespoke schema-init step in chat-service) for one table — that would
be more new infrastructure than the feature itself.

### 1.2 Pricing: one JSON file, not a database table, not hardcoded twice

Both C# and Python independently need the same model → price-per-million-tokens
mapping. There is no shared library between the two languages in this repo, so
whatever isn't centralized will drift. The fix: a single versioned file,
`config/gemini-pricing.json`, checked into the repo, read by both stacks at startup
(`File.ReadAllText` + `JsonSerializer` in chat-service/search-service; `json.load` in
ingestion-resx), cached in memory, never queried per-call.

Shape: one entry per model, with input-price-per-million-tokens and
output-price-per-million-tokens (output is irrelevant/zero for embedding models, but
keep the same shape for every model so the reader code doesn't need a special case).

Why a file and not a `model_pricing` Postgres table: a price change should be a
reviewed git commit with a diff and a reason, not a silent `UPDATE` statement with no
audit trail. If you later want to change prices without a redeploy, that's a
deliberate tradeoff to make explicitly — not the default.

## 2. Interception: inside the 3 chokepoints, nothing else touched

In all three cases, the usage-logging call happens **inside** the existing chokepoint
method, after the HTTP response is parsed, before returning to the caller. The
chokepoint's own return type/signature does not change. `RepairOrchestrator.cs`'s two
call sites, `TranscribeAsync`, and every ingestion-resx caller of `embedder.embed()`
need zero changes — logging is a side effect fully encapsulated where the call
already happens.

- **`GeminiChatClient.GenerateAsync`**: `UsageMetadata` is deserialized from the real
  top-level `usageMetadata` JSON field (including `thoughtsTokenCount` - see the
  explainer above for why that field matters). After deserializing, hand
  `(operation, model, usageMetadata)` to the usage logger before constructing/
  returning the `GeminiTurn`. `operation`/`sessionId` are extra parameters on
  `GenerateAsync` (e.g. `"routing"` / `"formatting"` / `"transcription"`) - the two
  `RepairOrchestrator` call sites and `TranscribeAsync` each already know which one
  they are.
- **`QueryEmbedder.EmbedAsync`**: no usage metadata exists to deserialize. Compute an
  estimated token count from the input `text`'s length using the pricing file's
  documented characters-per-token assumption, mark `is_estimated = true`, hand it to
  the same kind of logger.
- **`embedder.py`'s `Embedder.embed`**: same as `QueryEmbedder` — estimate from input
  length, `is_estimated = True`. Since this script already has the full document/
  symptom text available, the estimate input is whatever text was actually sent to
  `embed_content`, not a guess at it.

## 3. Writing the row: don't let logging slow down or break the chat response

chat-service and search-service are live, request/response, SSE-streaming services —
the mechanic is waiting on the actual answer. The Postgres write for a usage row must
never sit on that critical path, and a Postgres hiccup must never turn into a 500 for
the chat request.

Implemented pattern for both C# services: an in-process bounded
`System.Threading.Channels.Channel<UsageRecord>` (`UsageLogger`), written to
(non-blocking, `TryWrite`) from inside the chokepoint, drained by a single
`BackgroundService` (`UsageLogBackgroundService`) that batches rows (up to 25, or
every 5 seconds, whichever comes first) and inserts them in one connection per flush.
A Postgres failure during flush is caught and logged as a warning - never allowed to
crash the background service or affect the request path that produced the rows
(verified live by stopping Postgres mid-conversation - see section 7, Phase 3's gate).

Tradeoff accepted explicitly: a handful of usage rows can be lost if the process is
killed between `TryWrite` and the next batch flush. For a cost dashboard (not a
billing system of record), that's the right tradeoff — no write-ahead log or outbox
table was added for this.

ingestion-resx is different in kind, not just language: it's a one-shot batch script,
not a live server. There is no latency to protect and no concurrent traffic. Usage
rows are accumulated in a plain Python list across the run (`Embedder.usage_records`,
same shape as how `seeder.py` already accumulates `edges` before one bulk
`execute_values` insert), and bulk-inserted at the end of the run, right after
`ingestion_log`'s own summary row is written (so its real `id` is available to attach
as `ingestion_run_id` on every usage row from that run).

## 4. Cost calculation, precisely

Computed once, at write time, inside the same logging path described in section 2-3
— never recomputed later, never computed in the frontend.

For chat-service rows (real measured tokens):
`cost_usd = (promptTokens / 1,000,000 * inputPrice) + (completionTokens / 1,000,000 * outputPrice)`,
where `completionTokens = candidatesTokenCount + thoughtsTokenCount` (see the
explainer above - folding these together was a real fix made during the build, not
part of the original plan, after a direct API call proved the two numbers alone don't
add up to Gemini's own reported total).

For embedding rows (estimated tokens, single rate, no input/output split):
`cost_usd = (estimatedTokens / 1,000,000 * embeddingPrice)`.

If a model name shows up that isn't in the pricing file (e.g. someone changes the
hardcoded `Model` constant in `GeminiChatClient`/`QueryEmbedder` without updating the
pricing file), the row is logged with `cost_usd = NULL` rather than silently using a
wrong or default price — same "never fabricate a number" principle as section 0. A
`NULL` cost is an honest signal to fix the pricing file; a wrong cost is not.

## 5. Backend aggregation API: lives in search-service, not a new service

chat-service gained a DB connection purely to *write* its own rows — it didn't also
become the dashboard's read API, and no new "dashboard service" was created either
(see the explainer at the top of this doc). search-service already had the right
shape for this: existing `OUR_DB`/Npgsql connection, existing raw-SQL aggregation
queries (`GraphSearchService`), and it's already the service nginx routes
general data-shaped requests to. Centralizing reads there means aggregation SQL
exists in one place, not duplicated across three services that each only know about
their own rows.

`UsageController` in search-service, three read endpoints (all parameterized raw SQL,
`GROUP BY`/`date_trunc`, consistent with the existing `GraphSearchService` style — no
new query-builder, no ORM):
- **`GET /api/usage/summary`**: total cost, total tokens, total calls, broken down by
  model and by service, over an optional date range. Powers the KPI cards.
- **`GET /api/usage/timeseries`**: cost/tokens/call-count bucketed by hour or day over
  a date range. Powers the "system load over time" chart.
- **`GET /api/usage/logs`**: paginated raw rows from `gemini_usage_log`, filterable by
  service/model/date range. Powers the data table. Always includes `is_estimated` in
  the response so the frontend can visually flag estimated rows rather than
  presenting them identically to measured ones.

nginx routes `/api/usage` to `search-service`, alongside the existing `/api/search`
rule - guarded by a shared-secret header (`X-Usage-Key`), since this is the one
endpoint group in the whole app that exposes real spend data (see section 8).

## 6. Frontend: this is the first feature that needs routing

Before this feature, `AppComponent` directly hosted the whole chat UI with no router.
Bolting a second full-page view onto that without routing would have meant manually
toggling between two giant template blocks in one component.

What was built:
- `provideRouter(routes)` added to `app.config.ts`; `app.routes.ts` defines `''`
  (the chat page) and `'usage'` (the dashboard).
- The chat shell (header + message list + input bar — previously all inline in
  `app.component.html`) was extracted into its own routed component,
  `ChatPageComponent`. `AppComponent` is now a thin shell with a `<router-outlet>`
  plus the header chrome that stays global across both routes (logo, theme toggle,
  confirmed-car badge, two new nav links).
- `ChatPageComponent`'s host needed `display: contents` for the same reason already
  documented in `message-list.component.css` - without it, the new component would
  introduce an extra box into `.app-shell`'s flex column and break the existing
  flex:1/fixed-height sizing of the message list and input bar.

New files, following the conventions already established by `ChatApiService` and
`chat.models.ts` exactly (same `fetch`-based pattern, same "mirrors the backend DTOs"
comment style):
- `usage.models.ts` — TS interfaces mirroring the three search-service response
  shapes from section 5, 1:1.
- `usage-api.service.ts` — `UsageApiService`, `@Injectable({ providedIn: 'root' })`,
  one method per endpoint, relative `/api/usage/...` URLs, sends the `X-Usage-Key`
  header (see section 8).
- `components/usage/` (mirroring the existing `components/cards/`, `components/chat/`
  structure): `kpi-card`, `usage-chart`, `usage-log-table`, composed inside
  `usage-dashboard` (the routed page component).

KPI cards and the log table reuse the app's existing theme tokens
(`bg-surface`/`border-border`/etc.) and component patterns already established
throughout this build — no new visual language for this one page.

**Chart**: a hand-built SVG bar chart (plain Angular template, one `<rect>` per time
bucket, `<title>` for a plain hover tooltip) — no charting library exists in this app,
and an internal usage dashboard at this scale didn't need one. Richer interactivity
(zoom, multiple overlaid series) is a real v2 problem, not a v1 prerequisite.

## 7. Build pipeline (as executed)

Six phases, built and verified strictly in sequence - each phase's gate used real
data produced by the phase before it.

**Phase 0 — Schema and pricing, no behavior change.** Added `gemini_usage_log` to
`schema.sql` and `config/gemini-pricing.json`, mounted read-only into the 3 services
that need it. Verified the table existed via `psql` with zero other behavior change.

**Phase 1 — `ingestion-resx`.** Instrumented `Embedder.embed`/`seeder.py`. Rather than
forcing a full re-embed of the ~1,070-call corpus just to prove the plumbing worked, a
small throwaway script exercised the real `Embedder`/`log_usage` path against the real
Gemini API and real database at small scale - verified the exact reported codes/costs
against the raw resx text, then cleaned up the test rows (they referenced a fake
"not a real ingestion run" placeholder, unlike Phase 2/3's rows, which are real usage
and were kept).

**Phase 2 — `search-service`.** Instrumented `QueryEmbedder.EmbedAsync`. Verified via
one real search request through nginx; cost cross-checked by hand (40 chars ÷ 4 = 10
tokens × $0.15/1M = $0.0000015 - exact match).

**Phase 3 — `chat-service`** (highest risk: first-ever DB connection, on the live
SSE-streamed response path). Added `OUR_DB`, the `Channel`+`BackgroundService`
writer, instrumented `GeminiChatClient.GenerateAsync`. Found and fixed the
`thoughtsTokenCount` bug here (see the explainer above) via a real chat turn whose
`prompt + candidates` didn't equal `total` - confirmed the missing field via a direct
raw curl to the real Gemini API, fixed the formula, re-verified the math reconciled
exactly. Then proved resilience: stopped `our-postgres` mid-conversation, sent a
message with no tool-call dependency (so no *unrelated* Postgres need could mask the
result), confirmed the chat still answered correctly in under a second while the
background drain logged a caught warning instead of crashing; restarted Postgres and
confirmed clean recovery.

**Phase 4 — Aggregation API.** Built `UsageController`/`UsageQueryService` in
search-service, added the `/api/usage` nginx route. Every endpoint's output was
cross-checked against an independent manual `psql` aggregate query over the same
rows - exact matches.

**Phase 5 — Frontend.** Introduced routing, extracted `ChatPageComponent`, built the
dashboard page. Verified the rendered KPI numbers matched Phase 4's already-verified
figures exactly, in both light and dark theme, with the chat page unaffected.

**Phase 6 — Access guard.** Asked the user directly which guard to use (rather than
picking one autonomously) since this is a real security/exposure decision the doc
can't make on its own; shared-secret header was chosen and implemented exactly as
described in section 8 below. Verified all 4 states via curl (no header / wrong
header / correct header / unrelated route unaffected), then the full
prompt-store-retry flow via a real browser.

## 8. Access guard (as implemented)

This app has no authentication anywhere else - the usage dashboard is the first
feature that exposes data sensitive enough to need one. Implemented as a
shared-secret header, explicitly framed as a deterrent, not real security:

- `USAGE_DASHBOARD_KEY` lives in `.env` only, alongside the project's other secrets
  (`GEMINI_API_KEY`, `POSTGRES_PASSWORD`).
- `nginx.conf` is mounted into `/etc/nginx/templates/*.template` (not `/conf.d`
  directly) so the official nginx image's own `envsubst`-templating entrypoint
  substitutes `${USAGE_DASHBOARD_KEY}` with the real value at container startup - the
  real secret is never hand-edited into the committed config file.
- The `/api/usage` location block compares `$http_x_usage_key` against that value and
  returns 403 on any mismatch (including a missing header entirely).
- The frontend (`usage-api.service.ts`) prompts for the key once via `window.prompt`,
  stores it in `localStorage`, and sends it as `X-Usage-Key` on every `/api/usage/*`
  request. A 403 clears the stored key so the next request re-prompts instead of
  failing silently forever with a key that will never work.
- The mechanic-facing chat UI never sees or needs this key at all - only whoever
  actually opens `/usage` is ever prompted.

Because the frontend sending this header is a public static JS bundle, the secret is
visible to anyone who looks for it (network tab, `localStorage`) - this is
deliberately documented as a "don't index this page" deterrent, not an authorization
boundary, both in the nginx config's own comments and here.

## 9. Other ideas worth keeping in mind (not required for v1)

- **Retention/rollup**: at internal-dashboard scale this table won't need it for a
  long time, but if call volume grows, a periodic job that rolls granular rows older
  than N days into daily aggregates (and deletes the originals, or moves them to a
  cold table) keeps the table bounded without losing the KPI history. This is a
  problem to solve when it's real, not in advance.
- **Per-session cost surfaced in the chat UI itself**: since `session_id` is already
  on every chat-service row for free, a small "this conversation: $0.00X" indicator
  could eventually live in the chat header itself, not just the separate dashboard —
  same data, different consumer.
- **Per-ingestion-run cost surfaced next to the existing `ingestion_log` table**:
  since `ingestion_run_id` ties usage rows back to a specific run, "how much did this
  ingestion run cost" becomes a one-line join against data you already have, with no
  new concept to introduce.
- **Budget alerting**: a scheduled query against `gemini_usage_log` (e.g. "cost in
  the last 24h exceeds $X") that writes a flag somewhere the dashboard surfaces — a
  natural extension once the base table is real, not a v1 requirement.
- **Stronger access control**: the current shared-secret header is a deliberate
  deterrent, not real security (see section 8). If this app ever gains real
  authentication for any reason, `/api/usage` is the first thing that should move
  behind it instead of its own bespoke header check.
