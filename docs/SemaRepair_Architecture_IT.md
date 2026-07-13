# SemaRepair_Architecture_IT

# SemaRepair Chatbot — Documentazione di Architettura e Ricostruzione

> **Stato:** Prototipo v1 completo — Pianificazione v2
**Ultimo aggiornamento:** Giugno 2026
**Autore:** Team di Sviluppo
> 

---

## Indice

1. [Panoramica del Progetto](about:blank#1-panoramica-del-progetto)
2. [Prototipo v1 — Cosa è Stato Costruito](about:blank#2-prototipo-v1--cosa-%C3%A8-stato-costruito)
3. [Perché lo Stiamo Ricostruendo](about:blank#3-perch%C3%A9-lo-stiamo-ricostruendo)
4. [La Scala Reale — Cosa Abbiamo Scoperto](about:blank#4-la-scala-reale--cosa-abbiamo-scoperto)
5. [GraphRAG — Il Nuovo Approccio di Ricerca](about:blank#5-graphrag--il-nuovo-approccio-di-ricerca)
6. [Nuova Architettura — Multi-Servizio](about:blank#6-nuova-architettura--multi-servizio)
7. [Decisioni Tecnologiche](about:blank#7-decisioni-tecnologiche)
8. [Strategia di Accesso ai Dati](about:blank#8-strategia-di-accesso-ai-dati)
9. [Gestione degli Errori e Resilienza](about:blank#9-gestione-degli-errori-e-resilienza)
10. [Domande per la Riunione con l’Azienda](about:blank#10-domande-per-la-riunione-con-lazienda)
11. [Roadmap di Sviluppo](about:blank#11-roadmap-di-sviluppo)
12. [Struttura delle Cartelle](about:blank#12-struttura-delle-cartelle)
13. [Registro delle Decisioni Chiave](about:blank#13-registro-delle-decisioni-chiave)

---

## 1. Panoramica del Progetto

### 1.1 Cos’è SemaRepair Chatbot

SemaRepair Chatbot è un assistente di riparazione basato su AI per meccanici
e officine. Permette ai meccanici di identificare il proprio veicolo,
descrivere un guasto o inserire un codice diagnostico (DTC), e ricevere
procedure di riparazione strutturate da una base di conoscenza di casi di
riparazione documentati.

Il chatbot è costruito sopra il database esistente di guide di riparazione
di SemaRepair (documenti GUP), che sono casi di riparazione strutturati
documentati dai tecnici e validati dai costruttori dei veicoli.

### 1.2 Contesto di Business

SemaRepair è una piattaforma usata da meccanici indipendenti e officine in
tutta Italia ed Europa. Il loro database contiene migliaia di guide di
riparazione che coprono veicoli di molte marche. Il campione del prototipo
copre FIAT, FORD, CITROEN, PEUGEOT e IVECO — il database di produzione
completo include molte più marche, da confermare alla riunione con l’azienda.

Ogni guida di riparazione è disponibile in quattro lingue:
italiano (IT), francese (FR), inglese (EN) e portoghese (PT).

### 1.3 Stato Attuale

| Elemento | Stato |
| --- | --- |
| Prototipo v1 | ✅ Completo |
| Fonte dati | Campione Excel (108 documenti, solo IT) |
| Lingue supportate | Solo italiano |
| Deployment | Docker Compose, locale |
| Accesso ai dati di produzione | ❌ In attesa di approvazione dell’azienda |
| Set completo di documenti | ❌ In attesa |
| Architettura v2 | 📋 In pianificazione |

---

## 2. Prototipo v1 — Cosa è Stato Costruito

### 2.1 Panoramica dell’Architettura

```
React Frontend (Vite + TypeScript)
        │
        │ SSE + REST
        ▼
ASP.NET Core 8 Backend (Monolite)
        │
        ├── RepairOrchestrator (function calling Gemini)
        ├── RepairPlugin (3 strumenti)
        ├── DocumentSearchService (vettoriale + full-text)
        ├── CarSearchService (SQL strutturato)
        └── EmbeddingStartupService (background)
        │
        ▼
PostgreSQL 16 + pgvector
        │
        ├── gup_rows (2000 righe grezze da Excel)
        ├── repair_documents (108 documenti + embedding)
        ├── repair_document_cars (666 collegamenti auto-documento)
        └── car_embeddings (157 configurazioni auto + embedding)
        │
        ▼
Python Dataseeder (caricamento unico da Excel)

        + Google Gemini API
          ├── gemini-2.5-flash (chat + function calling)
          ├── gemini-embedding-001 (vettori a 768 dimensioni)
          └── gemini-2.5-flash (trascrizione audio)
```

### 2.2 Stack Tecnologico

| Livello | Tecnologia | Versione |
| --- | --- | --- |
| Frontend | React + TypeScript + Vite | 18 / 5 |
| Server frontend | nginx | alpine |
| Backend | ASP.NET Core Web API | 8.0 |
| Database | PostgreSQL + pgvector | 16 |
| AI — Chat | Google Gemini 2.5 Flash | ultima |
| AI — Embedding | gemini-embedding-001 | 768 dim |
| AI — Trascrizione | Google Gemini 2.5 Flash | ultima |
| Caricamento dati | Python | 3.12 |
| Infrastruttura | Docker + Docker Compose | ultima |

### 2.3 Pipeline dei Dati

**Fase 1 — Caricamento grezzo**
Analizza `GUP_PER_IA.xlsx` e carica 2000 righe in `gup_rows`.

**Fase 2 — Estrazione documenti**
Raggruppa per `sigla_documento`, crea 108 righe in `repair_documents`.
Estrae i capitoli e costruisce `embed_text` e `search_vector`.

**Fase 3 — Collegamenti auto-documento**
Crea 666 righe in `repair_document_cars`.

**Fase 4 — Configurazioni auto**
Crea 157 righe in `car_embeddings` per ogni codice motore unico.

**Fase 5 — Generazione embedding (backend)**
Genera embedding a 768 dimensioni all’avvio tramite `gemini-embedding-001`.
Richiede ~5 minuti. Completamente idempotente.

### 2.4 Pipeline AI

Usa il **function calling nativo di Gemini** tramite `RepairOrchestrator`.

**Tre strumenti:**
- `FindCar(brand?, model?, yearFrom?, yearTo?, fuel?, engineCode?, kw?)` — SQL
- `SearchByFaultCode(faultCode, engineCode?)` — full-text + fallback ILIKE
- `SearchBySymptom(symptom, engineCode?)` — similarità vettoriale, topK=1

**Flusso a due richieste:**
1. Prima richiesta (senza `responseMimeType`) — Gemini rileva la chiamata allo strumento
2. Lo strumento viene eseguito su PostgreSQL
3. Seconda richiesta (con `responseMimeType: "application/json"`) — trasmette la risposta in streaming
4. I chunk SSE vengono inviati al frontend

### 2.5 Cosa Funziona

| Funzionalità | Stato |
| --- | --- |
| Identificazione veicolo in linguaggio naturale | ✅ |
| Filtri per anno, marca, modello, codice motore | ✅ |
| Ricerca per codice guasto (codici P/C/B/U) | ✅ |
| Ricerca semantica basata sul sintomo | ✅ |
| Selezione auto dai risultati della ricerca sintomo | ✅ |
| Visualizzazione del documento di riparazione (template SemaRepair) | ✅ |
| Input vocale tramite trascrizione Gemini | ✅ |
| Risposte in streaming SSE | ✅ |
| Conversazione multi-turno | ✅ |
| Gestione dei messaggi non legati alla riparazione | ✅ |
| Deployment con Docker Compose | ✅ |

### 2.6 Limitazioni Note

| Limitazione | Causa Principale |
| --- | --- |
| Solo italiano | Il campione Excel contiene solo dati IT |
| 108 documenti | Dati campione |
| La ricerca semantica restituisce documenti poco correlati | Similarità vettoriale imprecisa |
| Instradamento errato degli strumenti da parte di Gemini | Ambiguità del linguaggio naturale |
| Nessuna autenticazione | Solo prototipo |
| Parsing Excel fragile | CSV multi-colonna in una singola cella |

---

## 3. Perché lo Stiamo Ricostruendo

### 3.1 Problemi con l’Architettura Attuale

**Problema 1 — La ricerca semantica è imprecisa**
La similarità vettoriale misura il significato delle parole, non le
relazioni tecniche. Due documenti possono ottenere un’alta similarità
perché entrambi menzionano “motore” anche se descrivono sistemi
completamente diversi.

**Problema 2 — Nessuna relazione tra i dati**
Nessuna conoscenza esplicita che P2279 e P1102 compaiono insieme, che il
guasto della valvola EGR riguarda lo stesso sistema su marche diverse, o
che il motore 8140.43S compare su FIAT Ducato, CITROEN Jumper e
PEUGEOT Boxer.

**Problema 3 — Il filtro per codice motore è troppo rigido**
Solo corrispondenza esatta del codice motore. Se il codice motore
confermato non ha documenti, non restituisce nulla — anche quando esiste
un motore identico sotto un codice diverso.

**Problema 4 — Qualità dei dati dal parsing Excel**
Estrazione CSV fragile. Alcuni documenti avevano i campi Impianto,
Dispositivo e Causa mancanti.

**Problema 5 — Una sola lingua**
Il prodotto reale necessita di IT, FR, EN, PT — ognuna richiede embedding
separati.

### 3.2 Perché il Monolite Non Scala

| Problema | Impatto |
| --- | --- |
| 40.000+ embedding all’avvio | Backend non disponibile per ore |
| 4 lingue nello stesso processo | Una ricerca IT lenta blocca il meccanico FR |
| Nessun isolamento dei guasti | Un crash blocca tutto |
| Impossibile scalare la ricerca indipendentemente | Risorse sprecate |

---

## 4. La Scala Reale — Cosa Abbiamo Scoperto

### 4.1 Volume dei Documenti

| Stima | Documenti | Embedding (4 lingue) |
| --- | --- | --- |
| Conservativa | 5.000 | 20.000 |
| Realistica | 20.000 | 80.000 |
| Piattaforma completa | 60.000 | 240.000 |

### 4.2 Requisiti Multi-Lingua

```
id_documento = 199309631
  ├── 199309631_IT.resx  → Italiano
  ├── 199309631_FR.resx  → Francese
  ├── 199309631_EN.resx  → Inglese
  └── 199309631_PT.resx  → Portoghese
```

Gli embedding vengono generati per lingua. La ricerca è filtrata per lingua
prima del ranking. Gemini risponde nella lingua del meccanico.

### 4.3 Fonti dei Dati

- **Il loro SQL Server** — dati principali, tutti i documenti e i veicoli
- **File resx** — `{id_documento}_{LANG}.resx` sul loro server
- **Export Excel** — solo per il prototipo, non adatto alla produzione

### 4.4 Impatto sull’Architettura

| Decisione | Perché è obbligatoria |
| --- | --- |
| Multi-servizio | 4 lingue × grande set di documenti |
| Ingestion in background | 40.000+ embedding non possono girare all’avvio |
| Accesso diretto al DB preferito | La gestione manuale dei file resx a scala non è fattibile |
| GraphRAG | Sono necessarie relazioni esatte |

---

## 5. GraphRAG — Il Nuovo Approccio di Ricerca

### 5.1 Cos’è GraphRAG

GraphRAG collega ogni documento ai codici guasto che contiene, al sistema
che riguarda, al dispositivo coinvolto e ai veicoli a cui si applica —
creando una rete navigabile di conoscenza tecnica che viene attraversata
con query SQL esatte, non con punteggi di similarità.

### 5.2 Vector RAG vs GraphRAG

|  | Vector RAG | GraphRAG |
| --- | --- | --- |
| Metodo di ricerca | Similarità coseno | Traversata del grafo |
| Trova | Documenti che suonano simili | Documenti direttamente collegati |
| Precisione | Media | Alta |
| Falsi positivi | Comuni | Rari |
| Nuovo codice guasto | Richiede ri-embedding | Aggiungi un arco al grafo |
| Stesso guasto su marche diverse | Manca se la formulazione è diversa | Arco condiviso esplicito |
| Query vaga | Restituisce risultati poco correlati | Ricade sul vettore |

**Strategia combinata:** GraphRAG come metodo primario, similarità
vettoriale come fallback.

### 5.3 Design del Grafo di Conoscenza

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

### 5.4 Tipi di Nodo e Proprietà

### Car (Auto)

| Proprietà | Tipo | Esempio | Fonte |
| --- | --- | --- | --- |
| idMacchina | string | FI0370 | `ID_MACCHINA` |
| engineCode | string | 8140.43S | `CODICE_MOTORE_MACCHINA` |
| motorizzazione | string | 2.8 JTD 8v | `MOTORIZZAZIONE_MACCHINA` |
| fuel | string | Diesel | `ALIMENTAZIONE_MACCHINA` |
| kw | integer | 94 | `KW_MACCHINA` |
| cavalli | integer | 128 | `CAVALLI_MACCHINA` |
| yearFrom | integer | 2000 | `ANNO_INIZIO_MACCHINA` |
| yearTo | integer | 2002 | `ANNO_FINE_MACCHINA` |

### Document (Documento)

| Proprietà | Tipo | Esempio | Fonte |
| --- | --- | --- | --- |
| idDocumento | string | 199309631 | resx `<ID>` |
| siglaDocumento | string | GUP97380 | resx `<DOCNumber>` |
| tipoRis | string | GUP | resx `<TipoRis>` |
| title | string | Accensione spia avaria motore… | resx `<Titolo>` |
| infoDoc | string | FRENI | P0504 | C1215 | … | resx `<InfoDoc>` |
| impianto | string | Freni | capitolo Identificazione |
| dispositivo | string | Interruttore stop | capitolo Identificazione |
| anomalia | string | Accensione spia avaria motore… | capitolo Identificazione |
| causa | string | Interruttore stop difettoso | capitolo Identificazione |
| intervento | string | Verificare il corretto funzionamento… | capitolo Procedura |
| procedura | string | - - | capitolo Procedura |
| nota | string | Il componente difettoso… | capitolo Procedura |
| reliability | integer | 1 / 2 / 3 | capitolo Grado |
| language | string | it / fr / en / pt | suffisso del nome file |

**Esempi reali dai dati campione:**

| siglaDocumento | impianto | dispositivo | anomalia (breve) | causa |
| --- | --- | --- | --- | --- |
| GUP97380 | Freni | Interruttore stop | Accensione spia avaria motore | Interruttore stop difettoso |
| GUP97381 | Alimentazione carburante | Pompa alta pressione | Scarse prestazioni spia avaria | Pompa alta pressione malfunzionante |
| GUP97383 | Iniezione | Filtro gasolio | Calo prestazioni spia rossa | Filtro gasolio ostruito |
| GUP97385 | Iniezione | Candelette | Calo prestazioni spia rossa | Candelette compromesse |

### FaultCode (Codice Guasto)

| Proprietà | Tipo | Esempio |
| --- | --- | --- |
| code | string | P0504 |
| description | string | Relazione interruttore freno 1-2 |
| system | string | Freni |
| fullText | string | P0504 (Relazione interruttore freno 1-2 - Rapporto errato) |

### System (Sistema)

| Proprietà | Tipo | Esempio |
| --- | --- | --- |
| name | string | Iniezione |
| category | string | Motore |

| Sistema | Categoria |
| --- | --- |
| Iniezione | Motore |
| Alimentazione carburante | Motore |
| Freni | Sicurezza |
| Climatizzatore | Comfort |
| Elettronica | Elettrico |
| Cambio | Trasmissione |
| Sterzo | Telaio |

### Device (Dispositivo)

| Proprietà | Tipo | Esempio |
| --- | --- | --- |
| name | string | Interruttore stop |
| system | string | Freni |
| commonFault | string | Interruttore stop difettoso |

### Symptom (Sintomo)

| Proprietà | Tipo | Esempio |
| --- | --- | --- |
| description | string | spia motore accesa scarse prestazioni |

### 5.5 Tipi di Relazione

| Da | Relazione | A | Esempio |
| --- | --- | --- | --- |
| Brand | HAS_MODEL | Model | FIAT → Ducato |
| Model | HAS_ENGINE | Car | Ducato → F1AE0481C |
| Car | DOCUMENTED_IN | Document | F1AE0481C → GUP97380 |
| Car | SHARES_ENGINE_WITH | Car | FIAT Ducato 8140.43S ↔︎ CITROEN Jumper 8140.43S |
| Document | CONTAINS_FAULT | FaultCode | GUP97380 → P0504, C1215, B1024 |
| Document | AFFECTS_SYSTEM | System | GUP97380 → Freni |
| Document | INVOLVES_DEVICE | Device | GUP97380 → Interruttore stop |
| Document | HAS_SYMPTOM | Symptom | GUP97380 → testo dell’anomalia |
| Document | HAS_TRANSLATION | Document | GUP97380_IT → GUP97380_FR |
| FaultCode | RELATED_TO | FaultCode | P0504 ↔︎ C1215 ↔︎ B1024 |
| System | CONTAINS | Device | Freni → Interruttore stop |

### 5.6 Esempi di Traversata del Grafo

### ⚠️ Regola Fondamentale — L’Auto Deve Essere Confermata Prima di Mostrare Qualsiasi Documento

Il Search Service **NON DEVE MAI** restituire un documento di riparazione
completo a meno che l’auto non sia stata prima confermata dal meccanico.

```
SBAGLIATO:
  Meccanico: "spia motore accesa"
  Sistema:   → restituisce GUP97389 immediatamente   ❌

CORRETTO:
  Meccanico: "spia motore accesa"
  Sistema:   → mostra la lista di selezione auto
  Meccanico: seleziona FIAT Ducato F1AE0481C
  Sistema:   → restituisce GUP97389 per F1AE0481C   ✅
```

---

### Query 1 — Codice guasto, nessuna auto confermata

```
Meccanico: "P0504" — nessuna auto confermata

Grafo: FaultCode{P0504} ←CONTAINS_FAULT← Document ←DOCUMENTED_IN← Car
→ Restituisce la lista di selezione auto (NON il documento di riparazione)
→ Il meccanico seleziona → POI mostra il documento di riparazione
```

---

### Query 2 — Codice guasto con auto confermata

```
Meccanico: "P0504" — auto FORD Fiesta XUJN confermata

Grafo: Car{XUJN} →DOCUMENTED_IN→ Document →CONTAINS_FAULT→ FaultCode{P0504}
→ Restituisce i documenti collegati sia a XUJN sia a P0504
→ Applica la Regola 10 per la gestione dei documenti multipli
```

---

### Query 3 — Sintomo con auto confermata (Ibrido Grafo + Vettore)

```
Meccanico: "spia motore accesa scarse prestazioni" — F1AE0481C confermata

Passo 1: Grafo — ottieni tutti i documenti per questa auto:
  Car{F1AE0481C} →DOCUMENTED_IN→ [doc1, doc2, doc3, ...]

Passo 2: Riordinamento vettoriale ALL'INTERNO del set pre-filtrato:
  Genera embedding del sintomo → ordina per distanza coseno solo
  all'interno dei documenti dell'auto
  → Restituisce la corrispondenza migliore

Passo 3: Applica la Regola 10
```

---

### Query 4 — Sintomo, nessuna auto confermata (Symptom Embeddings + Grafo)

> **Perché NON gli embedding completi del documento:**
Gli embedding completi del documento mescolano titolo, impianto,
dispositivo, causa, intervento, procedura, nota e codici guasto in un
unico vettore. Questo diluisce il significato semantico e fa sì che
documenti poco correlati ottengano un punteggio alto. Il campo `anomalia`
da solo è una descrizione breve e precisa esattamente di ciò che il
meccanico descriverebbe — rendendolo un target di ricerca molto migliore.
> 

**Gemini pulisce prima la query (dentro la chiamata allo strumento):**

```
Il meccanico scrive:
  "ho un problema con climatizzatore ventola del radiatore funzionamento continuo"

Gemini riceve questo e prima di chiamare SearchBySymptom, lo pulisce:
  Rimuove: "ho un problema con" (puro riempimento — zero contenuto tecnico)
  Mantiene: "climatizzatore ventola del radiatore funzionamento continuo"
            (il nome del sistema E l'intera descrizione tecnica — entrambi mantenuti)

Gemini chiama:
  SearchBySymptom(symptom="climatizzatore ventola del radiatore funzionamento continuo")
```

> **Perché “climatizzatore” deve essere mantenuto:** è il nome di un
Sistema, non riempimento. Eliminarlo scarterebbe informazione tecnica
reale senza giustificazione. La regola di pulizia rimuove solo frasi
conversazionali (“ho un problema con”, “ho notato”, “c’è”) che non
hanno alcun significato tecnico — non rimuove mai un sistema, un
dispositivo, o una descrizione del comportamento. In caso di dubbio,
Gemini mantiene la parola.
> 

**Regola del system prompt per Gemini:**

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

**Flusso di ricerca dopo che Gemini ha pulito la query:**

```
Passo 1 — Valida il sintomo (vedi Regola 12)

Passo 2 — Rileva il tipo di parola chiave contro il grafo PRIMA della ricerca vettoriale:
  Controlla se qualche parte del sintomo pulito corrisponde a un nodo
  System o Device in modo esatto o stretto nel grafo.

  SELECT 1 FROM graph_edges
  WHERE to_type IN ('system', 'device')
    AND (to_id ILIKE '%climatizzatore%' OR to_id ILIKE '%ventola%')
    AND language = 'it';

  → "Climatizzatore" corrisponde a un nodo System ✅

  Se viene trovata una corrispondenza System/Device:
    → La traversata del grafo ha la priorità (Tipo di Ricerca 2, corrispondenza esatta)
    → SELECT from_id FROM graph_edges
      WHERE to_id = 'Climatizzatore' AND relation = 'AFFECTS_SYSTEM'
    → Restituisce direttamente i documenti collegati a questo Sistema — nessun vettore necessario

  Se NON viene trovata nessuna corrispondenza System/Device:
    → Ricade sul Passo 3 (ricerca vettoriale su symptom_embeddings)

  Questo passaggio impedisce al sistema di saltare una corrispondenza esatta
  nel grafo a favore di una stima vettoriale quando il meccanico ha già
  nominato un System o Device conosciuto.

Passo 3 — Ricerca vettoriale su symptom_embeddings (solo valori anomalia)
  — usata solo quando il Passo 2 non trova corrispondenza System/Device,
    o per restringere ulteriormente i risultati all'interno di un Sistema corrispondente:

  SELECT id_documento
  FROM symptom_embeddings
  WHERE language = 'it'
  ORDER BY embedding <=> $queryVector
  LIMIT 1;

  symptom_embeddings contiene SOLO valori anomalia:
    "Funzionamento continuo alla massima velocità della ventola del radiatore"
    "Ventilatore abitacolo non funzionante"
    "Mancato funzionamento ventola raffreddamento"

  Query: "climatizzatore ventola del radiatore funzionamento continuo"
  → "Funzionamento continuo alla massima velocità della ventola del radiatore"
     distanza = 0.06  ✅ corrispondenza molto vicina → GUP97468

  Mantenere "climatizzatore" nella query non danneggia questa corrispondenza —
  il campo Impianto di GUP97468 è esattamente "Climatizzatore", quindi la
  parola extra rafforza il risultato corretto invece di diluirlo.

Passo 4 — Grafo: estrai le auto dal documento corrispondente:
  SELECT from_id FROM graph_edges
  WHERE to_type = 'document' AND to_id = '199309714'
    AND relation = 'DOCUMENTED_IN';
  → [FI0370, FI0371, FI0372, FI0373, FI0374, FI0375]

Passo 5 — Restituisci la lista di selezione auto (NON il documento di riparazione)
  Il meccanico seleziona → auto confermata → nuova ricerca → mostra il documento
```

---

### Query 5 — Stesso motore tra marche diverse, correlato al motore, nessun documento trovato

```
CITROEN Jumper 8140.43S confermata — 0 documenti trovati
Categoria del sistema: Motore → fallback CONSENTITO

Fallback: Car{CI_8140.43S} →SHARES_ENGINE_WITH→ Car{FIAT_8140.43S}
          Trova i documenti per le auto collegate

Bot (trasparenza obbligatoria):
"Non ho trovato casi per il tuo CITROEN Jumper. Ho trovato per veicoli
 con lo stesso motore (8140.43S): FIAT Ducato, PEUGEOT Boxer.
 Le procedure potrebbero essere applicabili. Vuoi vedere?"
```

---

### Query 6 — Sistema non legato al motore, nessun fallback tra marche

```
CITROEN Jumper — "problema ai freni" — 0 documenti trovati
Categoria del sistema: Sicurezza → fallback NON CONSENTITO

Motivo: Gli assemblaggi dei freni sono specifici del telaio.
Condividere il codice motore non significa condividere i componenti dei freni.

Bot: "Non ho trovato casi per i freni sul tuo CITROEN Jumper.
     Il sistema frenante è specifico. Inserisci il codice guasto."
```

### Categoria del Sistema — Regole di Fallback sul Motore

| Categoria | Sistemi | Fallback |
| --- | --- | --- |
| Motore | Iniezione, Alimentazione carburante, Candelette | ✅ Consentito |
| Elettrico motore | Sensori motore, Gestione motore | ✅ Consentito |
| Sicurezza | Freni, ABS, ESP, Airbag | ❌ Non consentito |
| Trasmissione | Cambio, Frizione | ❌ Non consentito |
| Comfort | Climatizzatore, Ventilazione | ❌ Non consentito |
| Elettrico carrozzeria | Immobilizer, Luci | ❌ Non consentito |
| Telaio | Sterzo, Sospensioni | ❌ Non consentito |

### 5.7 Storage del Grafo — Implementazione PostgreSQL

```sql
-- Tabella degli archi del grafo
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

-- Embedding dei sintomi — solo valori anomalia, per lingua
-- Usato come punto di ingresso per la ricerca sintomo senza auto
-- Più preciso degli embedding completi del documento per la corrispondenza del sintomo
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

**Perché due tabelle di embedding separate:**

| Tabella | Contiene | Usata per |
| --- | --- | --- |
| `document_embeddings` | Testo completo del documento (titolo + impianto + dispositivo + causa + intervento) | Ranking del sintomo ALL’INTERNO dei documenti di un’auto confermata |
| `symptom_embeddings` | Solo campo anomalia | Trovare il documento giusto quando NESSUNA auto è confermata |

**Righe di esempio per GUP97380:**

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

### 5.8 Come il Servizio di Ingestion Costruisce il Grafo

```
Per ogni documento elaborato:

1. Analizza l'XML resx
   → Estrae impianto, dispositivo, anomalia, causa dall'Identificazione
   → Estrae intervento, procedura, nota dalla Procedura
   → Estrae i codici guasto da InfoDoc: regex [PCBU]\d{4}
   → Conta le stelle nel capitolo Grado per la reliability

2. Crea il nodo Document in PostgreSQL

3. Costruisce gli archi del grafo:
   a. Car → DOCUMENTED_IN → Document
   b. Document → CONTAINS_FAULT → FaultCode (ogni codice in InfoDoc)
   c. FaultCode → RELATED_TO → FaultCode (tutte le coppie nello stesso documento)
   d. Document → AFFECTS_SYSTEM → System (da impianto)
   e. Document → INVOLVES_DEVICE → Device (da dispositivo)
   f. Document → HAS_TRANSLATION → Document (collega IT/FR/EN/PT)
   g. Car → SHARES_ENGINE_WITH → Car (stesso engineCode, marca diversa)

4. Genera l'embedding completo del documento → salva in document_embeddings
   embed_text = sigla + titolo + impianto + dispositivo + anomalia + causa

5. Genera l'embedding dell'anomalia → salva in symptom_embeddings
   embed_text = SOLO il campo anomalia
   → più breve, più preciso, migliore come punto di ingresso per la ricerca sintomo
```

### 5.9 Perché Questo Risolve i Problemi Attuali

| Problema nella v1 | Soluzione GraphRAG |
| --- | --- |
| Vengono restituiti documenti poco correlati | La traversata del grafo restituisce solo documenti collegati |
| Ventilatore abitacolo per ventola radiatore | Nodi System diversi — nessun arco condiviso |
| Nessuna relazione tra codici guasto | Gli archi RELATED_TO collegano i codici che compaiono insieme |
| Filtro codice motore troppo rigido | SHARES_ENGINE_WITH trova documenti tra marche diverse |
| Impianto/Dispositivo mancanti | Campi presi direttamente dall’XML resx — nessuna estrazione |
| Embedding completi del documento imprecisi per i sintomi | symptom_embeddings usa solo l’anomalia |
| Parole di riempimento che diluiscono gli embedding della query | Gemini pulisce la query prima di chiamare SearchBySymptom |
| Solo una lingua | Filtro per lingua su tutte le tabelle |
| Documento mostrato prima della conferma dell’auto | Le regole di business nella 5.10 impongono l’auto-prima |

### 5.10 Regole di Business — Flusso della Conversazione

---

**Regola 1 — La conferma dell’auto è obbligatoria prima di mostrare qualsiasi documento di riparazione**

```
SE confirmed_car È NULL E si sta per restituire un documento di riparazione
ALLORA → FERMATI → restituisci la lista di selezione auto → attendi la conferma
```

---

**Regola 2 — Sintomo senza auto → mostra la lista di selezione auto**

```
SE sintomo E confirmed_car È NULL
ALLORA → ricerca vettoriale su symptom_embeddings
       → grafo: estrai le auto dal documento migliore
       → restituisci la lista di selezione auto (NON il contenuto di riparazione)
```

---

**Regola 3 — Codice guasto senza auto → mostra la lista di selezione auto**

```
SE codice guasto E confirmed_car È NULL
ALLORA → grafo: trova le auto con questo guasto
       → restituisci la lista di selezione auto (NON il contenuto di riparazione)
```

---

**Regola 4 — Il messaggio di conferma dell’auto viene sempre mostrato**

```
Il meccanico seleziona l'auto → conferma prima di procedere
"Veicolo confermato: FIAT Ducato 2.3 JTD 16v (F1AE0481C).
 Descrivi il problema o inserisci un codice guasto."
```

Il badge dell’auto è sempre visibile nell’intestazione.

---

**Regola 5 — L’auto confermata persiste per l’intera sessione**

```
Messaggio 1: "ho un FIAT Ducato F1AE0481C" → confermata: F1AE0481C
Messaggio 2: "spia motore accesa"          → usa F1AE0481C automaticamente
Messaggio 3: "P1671"                       → usa F1AE0481C automaticamente
```

---

**Regola 6 — Il reset azzera l’auto confermata**

```
Il meccanico clicca Reset
→ confirmed_car = null → storico della sessione azzerato → si riparte da zero
```

---

**Regola 7 — Marca + sintomo in un unico messaggio → identifica prima l’auto**

```
Meccanico: "ho una FIAT Ducato diesel del 2004 con la spia del motore accesa"

Passo 1 → FindCar(brand="FIAT", model="Ducato", fuel="Diesel", yearFrom=2004)
Passo 2 → Presenta le opzioni auto
Passo 3 → Salva il sintomo originale: "spia del motore accesa"
Passo 4 → Il meccanico confema l'auto
Passo 5 → Nuova ricerca automatica: SearchBySymptom(saved_symptom, confirmed_engineCode)
Passo 6 → Mostra il documento di riparazione
```

---

**Regola 8 — SHARES_ENGINE_WITH richiede trasparenza esplicita**

```
MAI: restituire il documento FIAT Ducato per CITROEN Jumper senza spiegazione
SEMPRE: "Non ho trovato per il tuo CITROEN Jumper. Trovato per veicoli
         con lo stesso motore (8140.43S). Vuoi vedere?"
```

Solo per le categorie Motore ed Elettrico motore.

---

**Regola 9 — Un sintomo vago attiva una richiesta di chiarimento**

```
SE sintomo ≤ 4 parole E 0 risultati
ALLORA: "Puoi descrivere meglio?
       - Quale spia si accende?
       - Quando si manifesta?
       - Ci sono rumori anomali?
       - Hai un codice dal diagnostico?"
```

---

**Regola 10 — Documenti multipli trovati per l’auto confermata**

| Conteggio | Stesso guasto/dispositivo? | Azione |
| --- | --- | --- |
| 0 | — | Fallback SHARES_ENGINE_WITH (solo Motore) o non trovato |
| 1 | — | Mostra il documento di riparazione direttamente |
| 2–4 | SÌ (stesso DTC o dispositivo) | Mostra tutti ordinati per reliability ★★★ prima |
| 2–4 | NO (sistemi diversi) | Mostra la lista di selezione con impianto + dispositivo + causa |
| 5+ | — | Riordinamento vettoriale → mostra i primi 3 + “Puoi essere più specifico?” |

---

**Regola 11 — Pulizia della query da parte di Gemini**

Gemini pulisce il sintomo dentro la chiamata allo strumento `SearchBySymptom`.
Non serve nessun filtro nel codice — Gemini lo gestisce in modo naturale.

**Istruzione del system prompt:**

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

**Regola 12 — Livello di validazione nel Search Service**

Ogni chiamata a `SearchBySymptom` passa attraverso una validazione prima
della ricerca. Questo intercetta gli errori di Gemini in silenzio, senza
mostrare errori al meccanico.

```
SearchBySymptom(symptom, engineCode) chiamato
        │
        ▼
ValidateSymptom(symptom)
        │
        ├── TooVague
        │   Si attiva quando: vuoto, < 2 parole, solo stopword
        │   Azione: restituisce { resultType: "vague" }
        │           il Chat Service chiede chiarimenti
        │           confirmed_car NON azzerata (Regola 13)
        │
        ├── RedirectToFaultCode
        │   Si attiva quando: il symptom corrisponde a ^[PCBU]\d{4}$
        │   Azione: chiama internamente SearchByFaultCode(code, engineCode)
        │           restituisce il risultato del codice guasto in silenzio
        │           il meccanico vede il risultato corretto, nessun errore mostrato
        │
        └── Valid
            Azione: continua con la ricerca effettiva
                    Ibrido Grafo + Vettore (se auto confermata)
                    symptom_embeddings + Grafo (se nessuna auto)
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

    // Gemini ha passato accidentalmente un codice guasto come sintomo
    if (Regex.IsMatch(symptom.Trim(), @"^[PCBU]\d{4}$"))
        return ValidationResult.RedirectToFaultCode(symptom.Trim());

    // Solo parole generiche, nessun contenuto tecnico
    var stopwords = new[] { "problema", "errore", "guasto",
                             "non", "funziona", "rotto" };
    if (words.All(w => stopwords.Contains(w.ToLower())))
        return ValidationResult.TooVague("Solo parole generiche");

    return ValidationResult.Valid();
}
```

---

**Regola 13 — TooVague NON deve azzerare l’auto confermata**

```
Sessione: confirmed_car = F1AE0481C

Meccanico: "non funziona"
→ ValidationResult.TooVague
→ Chiede chiarimenti
→ confirmed_car RESTA = F1AE0481C  ← NON DEVE essere azzerata

Meccanico: "spia motore accesa scarse prestazioni"
→ La ricerca usa confirmed_car = F1AE0481C  ✅
```

---

**Albero Decisionale Completo:**

```
Il meccanico invia un messaggio
        │
        ├── Contiene marca/modello?
        │     SÌ → FindCar → selezione auto → conferma
        │           → salva il sintomo → nuova ricerca automatica
        │
        ├── Contiene un codice guasto (P/C/B/U + 4 cifre)?
        │     Auto confermata?
        │       SÌ → SearchByFaultCode(code, engineCode) → Regola 10
        │       NO  → grafo: auto con questo guasto → lista di selezione
        │
        ├── Contiene una descrizione di sintomo?
        │     → Gemini pulisce la query (Regola 11)
        │     → ValidateSymptom (Regola 12)
        │     Auto confermata?
        │       SÌ → Grafo: documenti per l'auto → Riordinamento vettoriale → Regola 10
        │       NO  → Vettore su symptom_embeddings → Grafo: auto → lista
        │
        ├── Contiene una parola chiave di sistema/dispositivo/causa?
        │     Auto confermata?
        │       SÌ → Grafo: System/Device → documenti per l'auto → Regola 10
        │       NO  → Grafo: auto con documenti per questo sistema → lista
        │
        ├── Messaggio non legato alla riparazione?
        │     → "Sono l'assistente SemaRepair. Descrivi il problema."
        │
        └── Troppo vago (Regola 9)?
              → Chiede maggiori dettagli (confirmed_car preservata secondo la Regola 13)
```

---

## 6. Nuova Architettura — Multi-Servizio

### 6.1 Perché il Multi-Servizio è Giustificato

- 40.000+ embedding richiedono ingestion in background, non caricamento all’avvio ✅
- 4 lingue richiedono percorsi di ricerca indipendenti ✅
- Il loro SQL Server + il nostro PostgreSQL = già due database ✅
- Lo streaming di Gemini richiede isolamento dalla ricerca pesante ✅
- La sincronizzazione notturna è una questione di background, non di richiesta ✅

### 6.2 Mappa dei Servizi

```
┌──────────────────────────────────────────────────────────┐
│                   React Frontend                          │
│         UI Multi-lingua (IT / FR / EN / PT)               │
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
        │  Il loro SQL Server (solo lettura)  │
        │  Il nostro PostgreSQL + pgvector    │
        └─────────────────────────────────────┘
                        ▲
               ┌────────┴────────┐
               │ Ingestion       │
               │ Service (notte) │
               └─────────────────┘
```

### 6.3 Chat Service

**Responsabilità:** Ricevere i messaggi, orchestrare le chiamate agli
strumenti di Gemini, trasmettere in streaming SSE.

**Endpoint:**
- `POST /api/chat/stream` — streaming SSE
- `POST /api/chat/transcribe` — Trascrizione audio tramite Gemini

**Dipendenze:** Gemini API, Search Service, Vehicle Service

### 6.4 Search Service

**Responsabilità:** GraphRAG + ricerca vettoriale per lingua con livello
di validazione.

**Endpoint:**
- `GET /api/search/fault-code?code=P2279&engine=XUJN&lang=it`
- `GET /api/search/symptom?q=ventola+radiatore&engine=F1AE0481C&lang=it`
- `GET /api/search/system?name=Iniezione&engine=F1AE0481C&lang=it`

**Contratto di risposta:**

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

> Valori di `resultType`:
- `document` — mostra il/i documento/i di riparazione, applica la Regola 10
- `car_selection` — nessuna auto confermata, mostra la lista di selezione
- `not_found` — nessun documento trovato
- `vague` — sintomo troppo generico, chiedi chiarimenti
- `redirected` — Gemini ha chiamato lo strumento sbagliato, corretto in silenzio
> 

**Dipendenze:** Il nostro PostgreSQL, il loro SQL Server, Gemini Embedding API

### 6.5 Vehicle Service

**Responsabilità:** Identificazione strutturata dell’auto via SQL.

**Endpoint:**
- `GET /api/vehicles?brand=FIAT&model=Ducato&yearFrom=2000&yearTo=2002&fuel=Diesel`
- `GET /api/vehicles?engineCode=F1AE0481C`
- `GET /api/vehicles/{idMacchina}`

**Contratto di risposta:**

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

**Responsabilità:** Sincronizzare i dati, costruire il grafo, generare
tutti gli embedding.

**Pianificazione:** Ogni notte alle 02:00 oppure trigger via webhook.

**Processo:**

```
1. Interroga il SQL Server per i documenti aggiornati dall'ultima esecuzione
2. Per ogni documento × 4 lingue:
   a. Analizza l'XML resx
   b. Estrae tutti i campi
   c. Genera document_embedding (embed_text completo)
   d. Genera symptom_embedding (solo anomalia)
   e. Salva entrambi in PostgreSQL
3. Costruisce tutti i 7 tipi di arco del grafo
4. Registra il log in ingestion_log
```

### 6.7 API Gateway

```
location /api/chat/     { proxy_pass http://chat-service:5000; }
location /api/search/   { proxy_pass http://search-service:5001; }
location /api/vehicles/ { proxy_pass http://vehicle-service:5002; }
location /              { proxy_pass http://frontend:80; }
```

### 6.8 Livello dei Dati

**Le nostre tabelle PostgreSQL:**

```sql
-- Embedding completi del documento
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

-- Embedding solo anomalia (punto di ingresso per il sintomo)
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

-- Embedding delle auto
CREATE TABLE car_embeddings (
    id           SERIAL PRIMARY KEY,
    id_macchina  TEXT NOT NULL,
    engine_code  TEXT NOT NULL,
    embed_text   TEXT,
    embedding    vector(768),
    created_at   TIMESTAMPTZ DEFAULT NOW()
);

-- Archi del grafo
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

-- Log dell'ingestion
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
```

---

## 7. Decisioni Tecnologiche

### 7.1 Perché ASP.NET Core

Stesso stack della v1, già dimostrato funzionante, tipizzazione forte,
EF Core + Npgsql, supporto Docker di prim’ordine.

### 7.2 Perché Python per l’Ingestion

Il miglior ecosistema per l’elaborazione di XML/dati, già familiare dal
dataseeder v1, più semplice per i job pianificati in background.

### 7.3 Perché PostgreSQL + pgvector

Già presente nella v1, indice HNSW per similarità rapida, archi del grafo
come semplice tabella, ricerca full-text integrata, gratuito e open source.

### 7.4 Perché Gemini

Già integrato, `gemini-2.5-flash` veloce ed economico, function calling
nativo, `gemini-embedding-001` a 768 dimensioni, supporta IT/FR/EN/PT,
una singola API key per chat + embedding + trascrizione.

### 7.5 Perché Docker Compose

Già presente nella v1, multi-servizio senza la complessità di Kubernetes,
deployment semplice su VPS, health check tra i servizi.

---

## 8. Strategia di Accesso ai Dati

### 8.1 Il loro SQL Server (solo lettura)

```
Server={host};Database={db};User Id={readonly_user};
Password={pwd};TrustServerCertificate=True;
```

Tabelle necessarie: Documenti, Capitoli, Veicoli, mapping Veicolo-Documento.

### 8.2 Il nostro PostgreSQL (scrittura)

Memorizza solo ciò che generiamo. Non memorizza mai copie del loro
contenuto di riparazione.

Le definizioni complete delle tabelle sono nella Sezione 6.8.

### 8.3 Embedding Multi-Lingua

```
Documento 199309631:
  IT document_embedding → embed_text completo in italiano
  IT symptom_embedding  → anomalia solo in italiano
  FR document_embedding → embed_text completo in francese
  FR symptom_embedding  → anomalia solo in francese
  EN document_embedding → embed_text completo in inglese
  EN symptom_embedding  → anomalia solo in inglese
  PT document_embedding → embed_text completo in portoghese
  PT symptom_embedding  → anomalia solo in portoghese
```

Totale embedding per documento: 8 (4 lingue × 2 tabelle).

### 8.4 Pianificazione dell’Ingestion

```
Trigger 1 — Ogni notte alle 02:00
  Elabora tutti i documenti aggiornati dall'ultima esecuzione
  Stimato: 30–60 minuti per aggiornamento incrementale

Trigger 2 — Webhook (opzionale)
  L'azienda notifica quando vengono pubblicati nuovi documenti

Trigger 3 — Manuale
  Endpoint amministrativo per forzare una re-ingestion completa
```

---

## 9. Gestione degli Errori e Resilienza

### 9.1 Gemini API Non Disponibile

```
Chat Service:
  → Riprova 3 volte (backoff 1s, 2s, 4s)
  → Se tutte falliscono: "Il servizio AI è temporaneamente non disponibile."

Ingestion Service:
  → Riprova l'embedding fino a 5 volte
  → Salta il documento fallito, registra l'errore, continua
  → Segnala in ingestion_log
```

### 9.2 Il loro SQL Server Irraggiungibile

```
Vehicle Service: riprova 3 volte → 503 al Chat Service
Search Service: la traversata del grafo funziona ancora (solo il nostro PostgreSQL)
                il contenuto del documento non disponibile — restituisce solo ID e titoli
Ingestion: non può girare → registra l'errore → riprova la notte successiva
```

### 9.3 Il nostro PostgreSQL Non Disponibile

```
Search Service: non può cercare → 503 → il Chat Service avvisa il meccanico
Ingestion: non può scrivere → si ferma → riprova
Chat Service: il Vehicle Service funziona ancora per l'identificazione dell'auto
```

### 9.4 Crash di un Singolo Servizio

```
Politica di restart Docker: restart: unless-stopped
Health check: GET /health → 200 healthy / 503 degraded
nginx instrada solo verso i servizi sani
Gli altri servizi non sono interessati durante il restart
```

### 9.5 Fallimento dell’Ingestion

```
Un'ingestion parziale è sicura — i dati esistenti restano validi
I documenti falliti vengono registrati in ingestion_log
La prossima esecuzione pianificata riprova i documenti falliti
Il chatbot continua a funzionare con i dati esistenti
```

### 9.6 Errori di Validazione (Search Service)

```
TooVague → restituisce { resultType: "vague" }
           il Chat Service chiede chiarimenti
           confirmed_car NON azzerata

RedirectToFaultCode → reindirizza internamente in silenzio
                      il meccanico vede il risultato corretto

Entrambi i casi: nessun errore mostrato al meccanico
                 degradazione controllata, non crash
```

### 9.7 Contratto degli Health Check

```json
GET /health

200 OK (healthy):
{
  "status": "healthy",
  "service": "search-service",
  "database": "connected",
  "timestamp": "2026-06-01T10:00:00Z"
}

503 (degraded):
{
  "status": "degraded",
  "service": "search-service",
  "reason": "PostgreSQL connection failed",
  "timestamp": "2026-06-01T10:00:00Z"
}
```

---

## 11. Roadmap di Sviluppo

### 11.1 Fase 1 — Prima di Iniziare a Programmare (Settimane 1-2)

- [ ]  Riunione con l’azienda — risposta sull’accesso ai dati
- [ ]  Confermare il conteggio dei documenti e la disponibilità delle lingue
- [ ]  Ottenere lo schema del DB o l’export completo dei file resx
- [ ]  Progettare lo schema del grafo con la struttura reale dei dati
- [ ]  Configurare l’ambiente di sviluppo

### 11.2 Fase 2 — Fondamenta (Settimane 3-4)

- [ ]  Docker Compose con tutti gli scheletri dei servizi
- [ ]  Connessione al SQL Server / parsing dell’export resx
- [ ]  Costruire l’Ingestion Service — grafo + embedding (entrambe le tabelle)
- [ ]  Verificare tutte le 4 lingue, verificare gli archi del grafo, verificare symptom_embeddings

### 11.3 Fase 3 — Servizi (Settimane 5-8)

**Settimana 5 — Vehicle Service**
Filtraggio SQL, tutte le 4 lingue, health check, unit test.

**Settimana 6 — Search Service**
Traversata del grafo, ricerca vettoriale (document_embeddings + symptom_embeddings),
livello di validazione (TooVague, RedirectToFaultCode, Valid),
filtraggio per lingua, ricerca ibrida, Regola 10, health check, unit test.

**Settimana 7 — Chat Service**
Function calling di Gemini, streaming SSE, multi-lingua, trascrizione,
system prompt con la Regola 11 (pulizia query), tutte le 13 regole di
business, health check.

**Settimana 8 — API Gateway**
Routing nginx, SSL, rate limiting, routing degli health check.

### 11.4 Fase 4 — Integrazione (Settimane 9-12)

**Settimana 9:** Frontend — selettore lingua, riuso dei componenti v1, nuovi endpoint.

**Settimana 10:** Test di integrazione — tutti i flussi, performance,
multi-lingua, test di resilienza.

**Settimana 11:** Correzione di bug e rifinitura.

**Settimana 12:** Produzione — VPS, SSL, monitoraggio, documentazione.

### 11.5 Timeline

```
Settimana 1-2:   Riunione con l'azienda + accesso ai dati
Settimana 3-4:   Fondamenta + ingestion
Settimana 5:     Vehicle Service
Settimana 6:     Search Service
Settimana 7:     Chat Service
Settimana 8:     API Gateway
Settimana 9:     Frontend
Settimana 10:    Test di integrazione
Settimana 11:    Correzione bug
Settimana 12:    Produzione
```

---

## 12. Struttura delle Cartelle

### 12.1 Struttura del Repository

```
SemaRepair-v2/
├── services/
│   ├── chat/
│   ├── search/
│   ├── vehicle/
│   └── ingestion/
├── frontend/
├── nginx/
│   └── nginx.conf
├── docs/
│   └── SemaRepair_Architecture.md
├── docker-compose.yml
├── docker-compose.override.yml
├── .env.example
├── .env
└── README.md
```

### 12.2 Struttura del Servizio

```
services/search/
├── Controllers/
│   └── SearchController.cs
├── Services/
│   ├── GraphSearchService.cs
│   ├── VectorSearchService.cs
│   ├── SymptomSearchService.cs    ← usa symptom_embeddings
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
-"80:80"
-"443:443"
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

ingestion-service:
build: ./services/ingestion
restart:"no"
environment:
THEIR_DB: ${THEIR_DB_CONNECTION}
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
test:["CMD-SHELL","pg_isready -U ${POSTGRES_USER}"]
interval: 5s
timeout: 5s
retries:10
```

---

## 13. Registro delle Decisioni Chiave

### 13.1 Decisioni Prese

| Decisione | Scelta | Motivo |
| --- | --- | --- |
| Approccio di ricerca | GraphRAG + fallback vettoriale | Precisione del grafo + flessibilità del vettore |
| Punto di ingresso per la ricerca sintomo | symptom_embeddings (solo anomalia) | Più precisa degli embedding completi del documento |
| Pulizia della query | Gemini pulisce dentro la chiamata allo strumento | Gestisce tutte le lingue e formulazioni senza codice |
| Livello di validazione | TooVague / Redirect / Valid nel Search Service | Correzione silenziosa degli errori di Gemini |
| Architettura | Multi-servizio | Scala, 4 lingue, ingestion in background |
| Tecnologia di chat | ASP.NET Core 8 | Stessa della v1, già dimostrata |
| Tecnologia di ingestion | Python | Miglior ecosistema XML/dati |
| Database vettoriale | PostgreSQL + pgvector | Già presente nello stack |
| Storage del grafo | Tabella graph_edges su PostgreSQL | Nessun DB a grafo separato a questa scala |
| Provider AI | Google Gemini | Già integrato, tutte le 4 lingue |
| Infrastruttura | Docker Compose | Sufficiente per il VPS |
| Conferma dell’auto | Obbligatoria prima di qualsiasi documento | Previene procedure per il motore sbagliato |
| Fallback tra marche | Solo sistemi legati al motore | I sistemi specifici del telaio differiscono per marca |
| Auto confermata su TooVague | Preservata | Il meccanico non deve riconfermare dopo un messaggio vago |

### 13.2 Decisioni in Sospeso

| Decisione | Opzioni | Bloccata da |
| --- | --- | --- |
| Metodo di accesso ai dati | SQL Server diretto / export resx / API | Riunione con l’azienda |
| Conteggio totale documenti | 5.000 / 20.000 / 60.000 | Riunione con l’azienda |
| Marche veicolo nello scope | Prototipo: 5 / DB completo: sconosciuto | Riunione con l’azienda |
| Autenticazione | Nessuna / API key / JWT | Requisiti di business |
| Storico conversazioni | Memorizzare / non memorizzare | Business + legale |
| Hosting | VPS / cloud / server azienda | Business |
| Meccanismo di aggiornamento | Cron notturno / webhook | Riunione con l’azienda |
| Gestione immagini | Includere / escludere | Riunione con l’azienda |
| Cache Redis | Aggiungere per il Vehicle Service / saltare | Test di performance |

---

*Documento versione 3.0 — Giugno 2026Prossima revisione: dopo la riunione con l’azienda*