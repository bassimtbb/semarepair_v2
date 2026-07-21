# SemaRepair v2 — Conversation Handoff

> Compacted context for continuing work in a new conversation.
> Read alongside `progress.md` (the full technical log, ~3300 lines) and
> `docs/TODO.md` (the actionable checklist).

---

## 1. What the project is

**SemaRepair v2** — an AI repair assistant for car mechanics. A mechanic
describes a problem or enters a fault code, confirms their exact vehicle,
and receives the correct documented repair procedure.

**Architecture: GraphRAG, not pure vector RAG.** The core insight: word
similarity is not correctness. A vector search for "radiator fan" also
surfaces "cabin fan" — different part, different system. So a relationship
graph is the *primary* retrieval path (exact traversal, no scoring), and
vector search is used only in two narrow places:

1. `symptom_embeddings` (anomalia field only) — entry point when no car is
   confirmed, to find *which* document; the graph then extracts candidate cars.
2. `document_embeddings` (full text) — used only to *rerank* within a
   graph-prefiltered set once a car is confirmed.

**The non-negotiable business rule:** a repair document is never shown
until a vehicle is confirmed (Rule 1).

**Stack:** PostgreSQL + pgvector, .NET microservices (Vehicle, Search,
Chat), Python ingestion (xlsx + resx), Angular 19 frontend with Tailwind
v4, nginx gateway, all in Docker Compose. Gemini for routing/formatting/
transcription; Google Cloud TTS for premium voice.

---

## 2. What is built and verified

| Component | State |
|---|---|
| Ingestion (xlsx) | `GUP_PER_IA.xlsx` → `gup_rows` (666 rows), idempotent |
| Ingestion (resx) | 540 files → 7 graph edge types + both embedding tables, real Gemini embeddings |
| Vehicle Service | Structured SQL car lookup, year-overlap semantics, suggested-year-range fallback |
| Search Service | GraphRAG + vector, 3 endpoints (fault-code/symptom/system), validation layer, Rule 8 cross-brand, Rule 10 all branches incl. 5+ |
| Chat Service | Gemini function-calling, SSE streaming, audio transcription, session state, Rules 1–13 |
| Frontend | Angular + Tailwind, light/dark theming, Lucide icons, responsive car grid, usage dashboard |
| Voice mode | 3 buttons: 🎤 Mic (free), 🔊 Voice/Web Speech (free), ✨ Voice HD (Google Cloud TTS, ~$0.80/1000 responses) |
| Numbered selection | Cars AND documents: badges + click / typed number / spoken number, 1–30, 5 languages |
| Languages | IT / EN / FR / PT / ES with auto-detection (tinyld) |

**Known test data (reuse — these are proven to hit the right paths):**

| What | Value |
|---|---|
| Car with many docs | `FI2504` — FIAT Ducato, engine `F1AE0481D` |
| Multi-brand shared engine | `8140.43S` — FIAT/CITROEN/PEUGEOT/IVECO, 14 cars |
| Cross-brand fallback (Rule 8) | `CI0037` CITROEN Jumper (engine `RHV`) + `P0380` → doc from `FI0393` |
| 3 documents | `P0638` + `F1AE0481D` |
| 14 documents (Rule 10) | System `Iniezione` + `F1AE0481D` |
| Exactly 1 document | `B1021` + engine `250 A 1000` |
| 5 IVECO trims, same engine | `8140.43S` (35C-13 / 35S-13 / 40C-13 / 45C-13 / 50C-13) |
| **Byte-identical embeddings** | `199309673` / `199309676` — identical anomalia text |

---

## 3. The working method (this is why the project is in good shape)

Established through repeated hard experience — keep doing this:

- **Measure, don't eyeball.** Contrast ratios calculated, not judged. Cosine
  distances queried, not assumed. Network payloads captured, not inferred.
- **Verify against live data in a real browser**, not "it compiles."
  `dotnet build` passing is necessary, never sufficient.
- **Read the actual exception/log before theorising.** Multiple bugs were
  misdiagnosed by plausible guessing and only solved by reading the trace.
- **Re-run previously-fixed bugs as regression tests**, not closed tickets.
- **Flag deviations, don't silently improvise.** Every architecture-doc
  deviation is logged with reasoning in `progress.md`.
- **LLM behaviour needs repeated trials**, not one pass (see §4).

---

## 4. The biggest finding of the last session

**`temperature=0` does NOT make `gemini-2.5-flash` deterministic.**

Empirically proven: with `temperature=0` alone, **3 of 8 runs of the same
query produced no tool call at all** — silently routing to a text response
instead of a search. The thinking chain is sampled independently of output
temperature.

**Fix:** `thinkingBudget=0` on the *routing* call (a mechanical
classification task — reasoning adds nothing). After this: 5/5 identical
across all test sequences. The *formatting* call keeps thinking + default
temperature (prose quality benefits, variation is harmless).

**Also discovered:** `GeminiChatClient` had **never** set a temperature on
any call. Every LLM decision in the system had been running at non-zero
temperature with non-deterministic thinking. This retroactively explains
the Rule 7 replay needing 15 trials, and the intermittent nature of several
past bugs.

---

## 5. IN FLIGHT — the immediate next task

### Problem 1: a permanently-invisible document (IMPLEMENTED AND VERIFIED — see progress.md §21)

> **Status: IMPLEMENTED AND VERIFIED — see progress.md §21.** Boundary case
> reachable on local data (tied pair `199309673`/`199309676` positioned at
> ranks 5–6, identical distance 0.162261); re-query fired and returned both
> tied docs where a plain `LIMIT 5` dropped one; 5× deterministic. Negative
> check confirms the re-query stays silent on normal queries (delta 0).
> `VectorSearchService` confirmed unaffected. The approved-fix and
> verification notes below are retained for the historical record.

**The bug we created:** adding `ORDER BY dist, id_documento` + `LIMIT 5` to
`SymptomSearchService.FindBestMatchesAsync` means that when two documents
have identical distances (confirmed real: `199309673`/`199309676`, byte-
identical anomalia text), the alphabetically-later one is **permanently,
silently, deterministically excluded** from every search. Worse than the
non-determinism it fixed — random ordering at least surfaced both sometimes.

Root cause: `GetTiedDocumentIds` (which handles ties correctly via
`TieThreshold = 0.02`) only sees what survived the SQL `LIMIT`. Truncation
happens before tie logic runs.

`VectorSearchService.RankWithinSetAsync` has no `LIMIT` → unaffected.

**Approved fix:**
1. SQL `LIMIT limit + 1` (one extra row is *sufficient* to detect a
   boundary tie — `limit * 2` was rejected as merely relocating the cutoff)
2. In C#: `cutoffDist = results[limit-1].Distance`; boundary tie exists if
   `results.Count > limit && results[limit].Distance == cutoffDist`
3. Only on detected tie: re-query with an **epsilon** filter (NOT exact
   float equality — `<= @cutoffDist + 1e-9`), no LIMIT, merge + dedupe
4. **Log when the re-query fires** (distance + resulting count) —
   observability, not truncation
5. No cap on the tied set; a 30-car list is the honest answer

**Invariant to hold:** a document tied with an included document is either
INCLUDED, or the system KNOWS it truncated and says so. Never silently gone.

**Why epsilon matters:** exact `=` on a float pits a C#-round-tripped
double against a value PostgreSQL computes fresh. Any precision difference
returns zero rows — recreating the original bug while the code believes it
handled it.

**Verification required:**
- Construct a query where `199309673`/`199309676` land at the boundary;
  confirm BOTH returned; show actual DB distances; show the log line
- Confirm re-query does NOT fire on normal queries (not silently running
  every time)
- Regression: "Problemi iniezioni" + `8140.43S` → 4 docs; "Iniezione" +
  `F1AE0481D` → Rule 10
- 5× determinism check
- Restate that `VectorSearchService` is unaffected

---

## 6. DONE — specificity routing prompt (was: next two tasks)

> **Status: both DONE — see progress.md §20.** Problem 2's specificity rule
> replaced the word-count rule; Problem 3's fault-word list was removed (not
> extended) because the specificity rule makes it redundant, as predicted
> below. Verified 5×/scenario live incl. words never on the old list
> (`iniettori rotti`, `iniettore difettoso`). Descriptions retained below
> for the historical record.

### Problem 2: replace the word-count rule with a specificity rule

The routing prompt currently contradicts itself: it states *"Minimo 3
parole tecniche"* while giving the 2-word worked example
`"Problemi iniezioni"`. Gemini has a rule and an immediate violation of it
in the same prompt.

**The deeper issue: word count is a bad proxy for specificity.**
- `"problemi iniezioni"` → 2 words, very specific (names a system)
- `"la macchina non va bene"` → 5 words, completely vague

The rule accepts the vague one and rejects the specific one. Replace it
with a specificity rule: *a query is searchable if it names something
concrete — a system, component, warning light, or observable behaviour; it
is vague only if it says something is wrong without saying what or where.*

### Problem 3: the fault-word list is a leaky bucket

The closed list (`problemi, problema, guasto, errore, anomalia, avaria`)
misses `rotti`, `difettoso`, `non funziona` — all of which hit the exact
original bug. A closed list of ways to say "broken" will always leak.

**Note:** Problem 2's specificity rule likely makes Problem 3 redundant —
if Gemini judges "does this name something concrete?" instead of matching
a word list, `"iniettori rotti"` passes naturally. Run Problem 2 first,
then check whether Problem 3 is still needed.

---

## 7. Recently fixed (don't re-litigate)

- **Rule 8 voice consent gate** — `foundViaSharedEngine` never resets
  across turns, so the guard uses `hasRealDocument` to distinguish the
  disclosure turn from the reveal turn. Frontend-held consent gate; no
  re-search, no LLM dependency.
- **Reset ghost history** — `SessionStore.Reset` used `TryRemove`, leaving
  a race window. Now clears fields in place, `History.Clear()` included.
- **IVECO trim identity** — `ConfirmedCarId` (`idMacchina`) is now sent,
  because engine+brand is ambiguous across 5 IVECO trims sharing `8140.43S`.
- **Document content fidelity** — `causa`/`intervento` etc. are spliced
  verbatim from Search Service into `ChatResponse`; never passed through
  Gemini's formatting call. Verified byte-identical.
- **Italian naming standardization** — `marca`/`modello`/`codiceMotore` etc.
  across Vehicle/Search/Chat + frontend, in 3 verified stages.
- **Routing fix** — fault words alongside a system name now route to
  `SearchBySymptom`, not `SearchBySystem` (this is what Problems 2/3 refine).
- **`gemini-2.0-flash-exp` retired by Google** → switched to a stable model.
  Lesson: never use `-exp`/preview model strings.
- **H1 — global JSON exception handler** — chat-service & vehicle-service now
  have search-service's `UseExceptionHandler` block, so an unhandled
  exception returns a JSON 503 instead of ASP.NET's default (empty/HTML)
  body. Vehicle path demonstrated live (Postgres stopped → Npgsql throw →
  JSON 503 via nginx). See progress.md §22. Chat's own unguarded path
  (the formatting Gemini call) is exercised by H2 — see docs/TODO.md.

---

## 8. Blocked on the company (not actionable locally)

- Production SQL Server access / full document count / complete brand list
- Auth model; conversation-history retention policy (GDPR-relevant)
- Hosting target (gates SSL, rate limiting, multi-instance)
- `SHARES_ENGINE_WITH` brand-filter confirmation (the architecture doc's
  own pseudocode contradicts its comment)
- `FI0427`/`FI0429` — genuinely distinct vehicles or data-entry duplicates?
- Which voice engine ships (🔊 free vs ✨ paid-but-better)

---

## 9. Known gaps still open locally

- **Zero automated tests** anywhere — all verification is manual
- `FaultCode.description` parsed from resx then **discarded** (no schema home)
- nginx caches backend IPs at startup → 502 after any independent container
  restart until nginx is also recreated (fix: Docker DNS + variable
  `proxy_pass`)
- In-memory sessions only (lost on restart, single-instance)
- No Gemini retry/backoff in Chat (doc specifies 3 retries)
- `gemini_usage_log` has no retention/pruning policy
- Real-browser mic (`audio/webm`) never tested with actual speech —
  only a synthetic tone
- Repair-card field labels hardcoded Italian regardless of session language
- Dead code: `SearchRequest` (never bound), `SessionStore.ConfirmCar`
  (never called)

---

## 10. How to continue

Give the new conversation this file plus, if needed, `progress.md` and
`docs/TODO.md`. The immediate task is **§5 — implement the approved
boundary-tie fix**, then **§6 Problem 2**, then check whether **Problem 3**
is still needed.

Maintain the working method in §3. In particular: this project has been
repeatedly saved by *reading the actual log before theorising* and by
*testing LLM behaviour across multiple runs rather than once*.
