# Graphe de connaissances — SemaRepair

Ce document décrit le graphe de connaissances **tel qu'il est réellement
construit dans le code**. Les sources vérifiées sont :

- `services/ingestion-resx/graph_builder.py` : construction des arêtes
- `services/ingestion-resx/schema.sql` : table `graph_edges`
- `services/search/Controllers/SearchController.cs` : utilisation de
  `SHARES_ENGINE_WITH` pour le fallback
- la base PostgreSQL en fonctionnement, pour les volumes

Le graphe est stocké dans une seule table PostgreSQL, `graph_edges`. Chaque
ligne est une arête `(from_type, from_id) —relation→ (to_type, to_id)`,
éventuellement associée à une langue (`language`). Les arêtes
`CONTAINS_FAULT` portent aussi une description (`description`).

---

## Figure 1 — Schéma du graphe (types de nœuds et relations)

Le **Document** est le nœud central : toutes les connaissances de réparation
(codes défaut, système, dispositif, traductions) s'y rattachent, et le
véhicule y accède par `DOCUMENTED_IN`. La relation **`SHARES_ENGINE_WITH`**,
en rouge, relie deux véhicules qui ont le même code moteur. C'est elle qui
permet le fallback intelligent.

```mermaid
flowchart LR
    CAR["<b>Véhicule</b><br/>type : car<br/>id : id_macchina<br/>ex. FI0393"]
    DOC(["<b>DOCUMENT</b><br/>type : document<br/>id : id_documento<br/>anomalie · cause · intervention"])
    FC["<b>Code défaut</b><br/>type : faultcode<br/>ex. P0380"]
    SYS["<b>Système</b><br/>type : system<br/>ex. Injection"]
    DEV["<b>Dispositif</b><br/>type : device<br/>ex. Fusible F17"]
    TR["<b>Document</b><br/>autre langue<br/>fr · en · es · pt"]

    CAR <==>|"SHARES_ENGINE_WITH<br/>même code moteur"| CAR
    CAR -->|"DOCUMENTED_IN"| DOC
    DOC -->|"AFFECTS_SYSTEM"| SYS
    DOC -->|"INVOLVES_DEVICE"| DEV
    DOC -->|"CONTAINS_FAULT"| FC
    DOC -->|"HAS_TRANSLATION<br/>version italienne → traductions"| TR
    FC -->|"RELATED_TO<br/>co-occurrence"| FC

    classDef hub fill:#1e3a8a,stroke:#0f1f4d,stroke-width:3px,color:#ffffff
    classDef car fill:#fee2e2,stroke:#dc2626,stroke-width:3px,color:#111827
    classDef entity fill:#eef2ff,stroke:#6366f1,stroke-width:1.5px,color:#111827
    classDef translation fill:#dbeafe,stroke:#1e3a8a,stroke-width:1.5px,stroke-dasharray:4 3,color:#111827
    class DOC hub
    class CAR car
    class FC,SYS,DEV entity
    class TR translation

    linkStyle 0 stroke:#dc2626,stroke-width:4px,color:#dc2626
```

---

## Figure 2 — Exemple réel : le fallback par moteur partagé

Données réelles de la base locale. Le mécanicien a confirmé un **CITROËN
Jumper** (moteur `RHV`) et cherche le code défaut **P0380**. Aucun document de
ce véhicule ne contient ce code. Le graphe suit alors `SHARES_ENGINE_WITH`
vers le **FIAT Ducato**, qui a le même moteur `RHV`, et trouve un document
pertinent. Le document est affiché en indiquant clairement qu'il provient
d'un véhicule d'une autre marque partageant le même moteur
(`foundViaSharedEngine = true`).

```mermaid
flowchart LR
    C1["<b>CITROËN Jumper</b><br/>CI0037 · moteur RHV<br/><i>véhicule confirmé</i>"]
    NONE["0 document<br/>contenant P0380"]
    C2["<b>FIAT Ducato</b><br/>FI0393 · moteur RHV"]
    DOC(["<b>Document 199310118</b><br/>« Impossibilité de démarrer<br/>le moteur après un arrêt en marche »"])
    FC["<b>P0380</b><br/>Commande temporisée des<br/>bougies de préchauffage 1"]
    FC2["<b>P1612</b><br/>Relais du réchauffeur<br/>du filtre à carburant"]
    SYS["<b>Injection</b>"]
    DEV["<b>Fusible F17</b><br/>protection du calculateur<br/>d'injection"]

    C1 -.->|"DOCUMENTED_IN"| NONE
    C1 <==>|"SHARES_ENGINE_WITH"| C2
    C2 -->|"DOCUMENTED_IN"| DOC
    DOC -->|"CONTAINS_FAULT"| FC
    DOC -->|"AFFECTS_SYSTEM"| SYS
    DOC -->|"INVOLVES_DEVICE"| DEV
    FC -->|"RELATED_TO"| FC2

    classDef hub fill:#1e3a8a,stroke:#0f1f4d,stroke-width:3px,color:#ffffff
    classDef car fill:#fee2e2,stroke:#dc2626,stroke-width:3px,color:#111827
    classDef entity fill:#eef2ff,stroke:#6366f1,stroke-width:1.5px,color:#111827
    classDef empty fill:#f3f4f6,stroke:#9ca3af,stroke-dasharray:5 5,color:#6b7280
    class DOC hub
    class C1,C2 car
    class FC,FC2,SYS,DEV entity
    class NONE empty

    linkStyle 1 stroke:#dc2626,stroke-width:4px,color:#dc2626
```

---

## Les 7 relations construites

Les volumes proviennent de la base locale : 157 véhicules, 108 documents en
5 langues (540 versions), et 3 663 arêtes au total.

| Relation | De → Vers | Construction (`graph_builder.py`) | Langue | Arêtes |
|---|---|---|---|---|
| `DOCUMENTED_IN` | Véhicule → Document | une arête par ligne de `gup_rows` (fichier `GUP_PER_IA.xlsx`) | — | 666 |
| `SHARES_ENGINE_WITH` | Véhicule ↔ Véhicule | toute paire de véhicules ayant le même `codice_motore_macchina`, **dans les deux sens** | — | 282 |
| `CONTAINS_FAULT` | Document → Code défaut | codes DTC extraits du resx, avec leur description | oui | 588 |
| `AFFECTS_SYSTEM` | Document → Système | champ `impianto` du resx | oui | 532 |
| `INVOLVES_DEVICE` | Document → Dispositif | champ `dispositivo` du resx | oui | 517 |
| `HAS_TRANSLATION` | Document → Document | version italienne (`<id>_it`) → chaque autre langue (`<id>_fr`, …) | — | 432 |
| `RELATED_TO` | Code défaut → Code défaut | codes apparaissant dans le même document (combinaisons par paire) | oui | 426 |

---

## Comment `SHARES_ENGINE_WITH` sert au fallback

Cette relation n'est utilisée qu'**après la confirmation du véhicule**, quand
la recherche directe ne donne rien. Le déroulement est le suivant :

```mermaid
flowchart TD
    A["Véhicule confirmé<br/>+ recherche (code défaut, système ou symptôme)"] --> B{"Document trouvé<br/>pour ce véhicule ?"}
    B -->|Oui| OK["Afficher le document"]
    B -->|Non| C{"Système lié<br/>au moteur ?"}
    C -->|Non| NF["Aucun résultat<br/>(freins, boîte, clim… ne se<br/>transposent pas entre marques)"]
    C -->|Oui| D["Suivre SHARES_ENGINE_WITH<br/>vers les véhicules au même moteur"]
    D --> E{"Document trouvé<br/>sur ces véhicules ?"}
    E -->|Non| NF
    E -->|Oui| F["Afficher le document<br/>avec la mention « trouvé via un<br/>véhicule au moteur partagé »"]

    classDef ok fill:#dcfce7,stroke:#16a34a,color:#111827
    classDef fallback fill:#fee2e2,stroke:#dc2626,stroke-width:3px,color:#111827
    classDef stop fill:#f3f4f6,stroke:#9ca3af,color:#374151
    class OK ok
    class D,F fallback
    class NF stop
```

Le fallback est limité aux **systèmes liés au moteur**, définis par une liste
blanche dans `SystemCategoryLookup.cs`. Elle contient les noms italiens stockés
dans les données : *Iniezione*, *Alimentazione carburante*, *Candelette*,
*Sensori motore*, *Gestione motore*, *Alimentazione motore*, *Sistema di
accensione* et *Sistema di scarico*. Un même moteur ne signifie pas que les freins ou
la climatisation sont identiques : on ne transpose donc que ce qui dépend
réellement du moteur.

---

## Écart avec la documentation d'architecture (§5.5)

La section 5.5 de `SemaRepair_Architecture.md` décrit un modèle conceptuel de
**11 relations**. Seules les **7 relations ci-dessus** sont implémentées. Les
4 suivantes ne sont pas construites :

| Relation prévue | Pourquoi elle n'est pas nécessaire aujourd'hui |
|---|---|
| `Brand —HAS_MODEL→ Model` | marque et modèle sont des colonnes de `gup_rows`, interrogées en SQL par le Vehicle Service |
| `Model —HAS_ENGINE→ Car` | idem : pas de nœuds Marque/Modèle séparés |
| `Document —HAS_SYMPTOM→ Symptom` | le symptôme (`anomalia`) est géré par recherche vectorielle (table `symptom_embeddings`), pas par une arête |
| `System —CONTAINS→ Device` | aucun parcours de recherche actuel ne l'utilise |
