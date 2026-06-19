# SemaRepair v2

AI repair assistant chatbot for car mechanics, built on a GraphRAG architecture
(PostgreSQL + pgvector + a relationship graph) instead of pure vector RAG.

Full design docs: [docs/SemaRepair_Architecture.md](docs/SemaRepair_Architecture.md),
[docs/EmbeddingAndGraph_Technical.md](docs/EmbeddingAndGraph_Technical.md).

## Status

Only the **ingestion** service exists so far — it loads `Data/GUP_PER_IA.xlsx`
into a `gup_rows` vehicle↔document mapping table. `chat/`, `search/`,
`vehicle/`, `frontend/`, and `nginx/` are placeholders for the planned
multi-service architecture.

## Structure

```
services/
├── chat/        — not yet built
├── search/      — not yet built
├── vehicle/     — not yet built
└── ingestion/   — xlsx -> gup_rows loader
frontend/        — not yet built
nginx/           — API gateway config (not yet wired into docker-compose)
docs/            — architecture documentation
Data/            — sample resx files + GUP_PER_IA.xlsx
```

## Running

```
docker compose up -d --build
```

Starts Postgres+pgvector and runs the ingestion service once to load
`gup_rows`.
