# SemaRepair v2

AI repair assistant for car mechanics. A mechanic describes a problem or
enters a fault code, confirms their exact vehicle, and gets the documented
repair procedure for that vehicle.

It uses **GraphRAG** (PostgreSQL + pgvector + a relationship graph) instead of
plain vector RAG, because similar words don't mean the same fix: "radiator
fan" and "cabin fan" are close as vectors but are different parts in
different systems. Graph traversal is the main retrieval path, and vector
search is used in only two places:

1. `symptom_embeddings`: finds candidate documents when no car is confirmed
   yet. The graph then gives the candidate cars.
2. `document_embeddings`: reranks a set the graph has already filtered, once
   a car is confirmed.

**Core rule (Rule 1):** no repair document is shown until the vehicle is
confirmed.

## Architecture

```
browser ──► nginx :80 ─┬─ /              → frontend        (Angular 19 + Tailwind v4)
                       ├─ /api/chat      → chat-service    :5000  (.NET 8, Gemini)
                       ├─ /api/search    → search-service  :5001  (.NET 8, GraphRAG + vector)
                       ├─ /api/usage     → search-service  :5001  (guarded by X-Usage-Key)
                       └─ /api/vehicles  → vehicle-service :5002  (.NET 8, SQL car lookup)

                          all backends ──► our-postgres (pgvector/pg16)
                                              ▲
             ingestion (xlsx) ──► ingestion-resx (resx + embeddings)   one-shot loaders
```

| Component | What it does |
|---|---|
| `services/ingestion` | Python. Loads `Data/GUP_PER_IA.xlsx` into `gup_rows`, the vehicle↔document mapping (666 rows). Idempotent. |
| `services/ingestion-resx` | Python. Parses 540 resx documents into 7 graph edge types, plus both embedding tables (Gemini `gemini-embedding-001`). Idempotent. |
| `services/vehicle` | Structured SQL car lookup with year-overlap matching and a suggested-year-range fallback. |
| `services/search` | Fault-code, symptom, and system search. Includes the validation layer, Rule 8 cross-brand shared-engine fallback, Rule 10 multi-document branches, and the Gemini usage/cost API. |
| `services/chat` | Orchestrates the conversation. Gemini function-calling for routing, formatting, and transcription. SSE streaming, in-memory sessions, and a Google Cloud TTS proxy. |
| `frontend` | Angular chat UI with light/dark themes, a numbered car and document picker (click, typed, or spoken numbers), voice mode, and a `/usage` cost dashboard. |
| `nginx` | API gateway. Resolves backends per request, so restarting one container doesn't cause 502s. |

Supported languages: Italian, English, French, Portuguese, and Spanish, auto-detected per message.

## Running

### 1. Configure

```
cp .env.example .env
```

Then fill in `.env`:

| Variable | Notes |
|---|---|
| `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD` | Local defaults in `.env.example` are fine. |
| `GEMINI_API_KEY` | **Required.** Google AI Studio key (`AIzaSy…` prefix) from aistudio.google.com/app/apikey. Keys starting `AQ.` return 401. Don't restrict it to TTS-only. |
| `GOOGLE_CLOUD_TTS_API_KEY` | Optional. A separate Google Cloud key for the ✨ Voice HD button. It can be restricted to the Text-to-Speech API. It stays on the server and never reaches the browser. |
| `USAGE_DASHBOARD_KEY` | Shared secret for `/usage`. It isn't in `.env.example`, so add it yourself. This only discourages casual access; it isn't real auth. |

### 2. Start the stack

```
docker compose up -d --build
```

The first run starts Postgres. It then runs `ingestion` and `ingestion-resx`,
each once and in that order, and starts the three services, the frontend, and
nginx. When it's up, open http://localhost.

`docker-compose.override.yml` is merged automatically. It publishes Postgres
(`5432`) and each backend (`5000`–`5002`) on the host so you can debug them
directly.

**Windows + OneDrive:** if the project folder is under OneDrive, BuildKit can
read the source files as 31-byte placeholders and the build breaks. Use the
wrapper script instead. It copies each build context to `%TEMP%`, builds from
there, and runs `docker compose up -d`:

```powershell
./build.ps1                          # all services
./build.ps1 -Only chat-service,frontend
```

### Re-seeding

Both ingestion steps skip work if their tables already have data. To reload,
`TRUNCATE` `gup_rows` and/or `graph_edges`, `document_embeddings`, and
`symptom_embeddings`, then run the loaders again.

## Frontend development

```
cd frontend
npm install
npm start          # ng serve on http://localhost:4200
```

`proxy.conf.json` sends `/api` to `http://localhost` (nginx), so the Docker
stack must be running.

## Tests

`tests/SemaRepair.IntegrationTests` holds xUnit integration tests. They call
the **running** stack over HTTP through nginx, using the real seeded Postgres
and real Gemini, so they need the stack up and a valid `GEMINI_API_KEY`. There
are no mocks.

```
dotnet test tests/SemaRepair.IntegrationTests
dotnet test tests/SemaRepair.IntegrationTests --filter "Category!=Slow"   # skip the 5-run determinism test
```

The tests currently cover:

- the boundary-tie re-query
- the Rule 1 gate on documents
- routing determinism
- system/device subject matching

Optional environment overrides:

- `SEMAREPAIR_BASE_URL` (default `http://localhost`)
- `SEMAREPAIR_SEARCH_CONTAINER` (default `semarepair_v2-search-service-1`)

There is no CI yet.

## Project layout

```
services/
├── chat/             Chat orchestration, Gemini, SSE, TTS proxy
├── search/           GraphRAG + vector search, usage API
├── vehicle/          Vehicle lookup
├── ingestion/        xlsx → gup_rows
└── ingestion-resx/   resx → graph_edges + embeddings
frontend/             Angular 19 app
nginx/                Gateway config (envsubst template)
config/               gemini-pricing.json (cost tracking)
tests/                Integration tests
Data/                 GUP_PER_IA.xlsx + sample resx documents
docs/                 Architecture, handoff, TODO
progress.md           Full technical log of decisions and verifications
```

## Documentation

- [docs/SemaRepair_Architecture.md](docs/SemaRepair_Architecture.md): full design ([Italian version](docs/SemaRepair_Architecture_IT.md))
- [docs/EmbeddingAndGraph_Technical.md](docs/EmbeddingAndGraph_Technical.md): embedding and graph internals
- [docs/VoiceMode_Architecture_v2.md](docs/VoiceMode_Architecture_v2.md): the three voice modes (mic, Web Speech, Cloud TTS)
- [docs/log-dashboard.md](docs/log-dashboard.md): Gemini usage logging and the `/usage` dashboard
- [docs/chat-test-scenarios.md](docs/chat-test-scenarios.md): manual test scenarios with known-good data
- [docs/SemaRepair_Handoff.md](docs/SemaRepair_Handoff.md): current state and working method
- [docs/TODO.md](docs/TODO.md): open fixes, enhancements, and items blocked on company decisions

## Known limitations

- Chat sessions are kept in memory, so a restart loses them and only one instance can run.
- There's no authentication, rate limiting, or TLS. These wait on the hosting and auth decisions.
- Gemini calls in the chat service aren't retried with backoff.
- The data is a local sample: 5 brands, about 108 documents. Production data access isn't set up yet.
