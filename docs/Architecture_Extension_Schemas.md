# Extension v2 — les schémas

> Accompagne [Architecture_Extension_v2.md](Architecture_Extension_v2.md).
> Aperçu Mermaid : `Ctrl+Shift+V` dans VS Code.

---

## 1. Ce que contiennent les 64 documents

Quatre familles. Une seule est exploitée aujourd'hui.

```mermaid
flowchart TD
    ALL["<b>64 documents</b><br/>livrés pour FI0396<br/>21 types différents"]

    A["<b>A · Fiches de panne</b><br/>type GUP · 36 documents<br/>anomalie · cause · intervention"]
    B["<b>B · Faits tabulaires</b><br/>FUS COP DTM FAR LCZ SCH · 12 documents<br/>226 faits + 113 légendes + 7 PDF"]
    C["<b>C · Prose technique</b><br/>DIS AZZ IND FLT NOT POP INF CIA INT · 10 documents<br/>36 chapitres · 15 790 caractères"]
    D["<b>D · Coquilles vides</b><br/>CDD PTG LGR RCD RGR · 6 documents<br/>liens externes uniquement"]

    ALL --> A
    ALL --> B
    ALL --> C
    ALL --> D

    A --> OK["<b>EXPLOITÉ</b><br/>le produit actuel"]
    B --> KO["<b>IGNORÉ</b><br/>seul le titre est lu"]
    C --> KO
    D --> NIL["<b>SANS VALEUR</b><br/>hors périmètre"]

    classDef total fill:#1e3a8a,stroke:#0f1f4d,stroke-width:3px,color:#ffffff
    classDef used fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#111827
    classDef lost fill:#fee2e2,stroke:#dc2626,stroke-width:2px,color:#111827
    classDef dead fill:#f1f5f9,stroke:#94a3b8,stroke-width:1.5px,stroke-dasharray:4 3,color:#475569
    class ALL total
    class A,OK used
    class B,C,KO lost
    class D,NIL dead
```

**À retenir :** 22 documents sur 64 contiennent de la vraie matière inexploitée. La
famille C est la surprise — elle n'était pas dans la proposition initiale, et c'est la
moins chère à récupérer.

---

## 2. Aujourd'hui vs demain

Ce qui change du point de vue du mécanicien.

```mermaid
flowchart LR
    subgraph AUJ["AUJOURD'HUI"]
        direction TB
        Q1["<i>« il motore non si avvia »</i>"] --> R1["Fiche de réparation"]
        Q2["<i>« quale fusibile per l'ABS »</i>"] --> R2["Rien"]
        Q3["<i>« mostrami lo schema airbag »</i>"] --> R2
        Q4["<i>« come azzero l'indicatore »</i>"] --> R2
    end

    subgraph DEM["DEMAIN"]
        direction TB
        P1["<i>« il motore non si avvia »</i>"] --> S1["Fiche de réparation<br/><small>inchangé</small>"]
        P2["<i>« quale fusibile per l'ABS »</i>"] --> S2["<b>F04 · 50 A</b><br/>Vano Motore"]
        P3["<i>« mostrami lo schema airbag »</i>"] --> S3["<b>Schéma PDF</b><br/>Airbag Siemens MY99"]
        P4["<i>« come azzero l'indicatore »</i>"] --> S4["<b>Procédure</b><br/>extraite du document AZZ"]
    end

    AUJ ~~~ DEM

    classDef ok fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#111827
    classDef ko fill:#fee2e2,stroke:#dc2626,stroke-width:2px,color:#111827
    classDef new fill:#dbeafe,stroke:#1e3a8a,stroke-width:2px,color:#111827
    class R1,S1 ok
    class R2 ko
    class S2,S3,S4 new
```

Le produit passe de *« je te trouve un document »* à *« je te donne la réponse »*.

---

## 3. Le pipeline d'ingestion

En vert ce qui existe et **n'est pas touché**. En bleu ce qu'on ajoute.

```mermaid
flowchart TD
    RESX[("<b>Data/resx_samples</b><br/>290 fichiers · 64 documents")]
    PDF[("<b>Data/PDF</b><br/>7 schémas PDF")]

    RESX --> P1["<b>resx_parser.py</b><br/>lit XCAPITOLO Ordine 1/3/4<br/><small>INCHANGÉ</small>"]
    RESX --> P2["<b>technical_parser.py</b><br/>NOUVEAU"]

    P1 --> T1[("<b>documents</b><br/>290 lignes")]
    P1 --> T2[("<b>graph_edges</b><br/>885 arêtes")]

    P2 --> F1["Gruppo / Dato /<br/>Valore / UnitaMis<br/><small>COP DTM FAR</small>"]
    P2 --> F2["Numero / Descrizione /<br/>Riferimento<br/><small>FUS LCZ SCH</small>"]
    P2 --> F3["Chapitres, tout Ordine<br/><small>famille C</small>"]

    F1 --> KC[("<b>knowledge_chunks</b><br/>NOUVELLE TABLE<br/>~1 700 lignes")]
    F2 --> KC
    F3 --> KC

    T1 --> E1["<b>embedder.py</b><br/><small>RÉUTILISÉ TEL QUEL</small>"]
    KC --> E1
    E1 --> V1[("document_embeddings<br/>symptom_embeddings")]
    E1 --> V2[("knowledge_chunks.embedding")]

    PDF -.->|"référencé par RifIDFilePDF"| KC

    classDef src fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#111827
    classDef keep fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#111827
    classDef add fill:#dbeafe,stroke:#1e3a8a,stroke-width:2.5px,color:#111827
    class RESX,PDF src
    class P1,T1,T2,E1,V1 keep
    class P2,F1,F2,F3,KC,V2 add
```

**Le point clé :** `resx_parser.py` n'est pas modifié. Il fait tourner la démo, on n'y
touche pas. Le nouveau parseur vit à côté.

---

## 4. Une seule table, pas trois

Le choix d'architecture le plus important.

```mermaid
flowchart LR
    subgraph NON["CE QU'ON NE FAIT PAS"]
        direction TB
        X1[("technical_facts")] --> X2[("fact_embeddings")]
        X3[("document_sections")] --> X4[("section_embeddings")]
        X5[("schema_legends")] --> X6[("legend_embeddings")]
    end

    subgraph OUI["CE QU'ON FAIT"]
        direction TB
        Y["<b>knowledge_chunks</b><br/>id_documento · language<br/><b>kind</b> = fact / section / legend<br/>heading · label · value · unit<br/>reference · body<br/>search_text · embedding"]
    end

    NON ~~~ OUI

    classDef bad fill:#fee2e2,stroke:#dc2626,stroke-width:1.5px,stroke-dasharray:4 3,color:#7f1d1d
    classDef good fill:#dbeafe,stroke:#1e3a8a,stroke-width:3px,color:#111827
    class X1,X2,X3,X4,X5,X6 bad
    class Y good
```

Six tables, trois parseurs, trois chemins de recherche — contre une table, un passage
d'embeddings, une requête. **Et surtout : `DROP TABLE knowledge_chunks` annule tout.**

C'est moins élégant sur le papier. C'est le bon choix pour un planning sans marge.

---

## 5. Le routage — la décision du modèle

Un seul outil nouveau, donc **une seule frontière** à faire comprendre au modèle.

```mermaid
flowchart TD
    MSG["Message du mécanicien"]
    Q{"Est-ce que<br/><b>quelque chose ne va pas</b> ?"}

    MSG --> Q

    Q -->|"OUI · un défaut"| DIAG["<b>Outils de diagnostic</b><br/><small>existants, inchangés</small>"]
    Q -->|"NON · il veut savoir"| INFO["<b>SearchTechnicalInfo</b><br/><small>NOUVEAU · outil unique</small>"]

    DIAG --> D1["SearchByFaultCode<br/><i>« ho il codice P0380 »</i>"]
    DIAG --> D2["SearchBySymptom<br/><i>« l'ABS non funziona »</i>"]
    DIAG --> D3["SearchBySystem<br/><i>« iniezione »</i>"]

    INFO --> K{"kind du chunk<br/>trouvé"}
    K -->|fact| I1["<b>Valeur</b><br/><i>F04 · 50 A</i>"]
    K -->|legend| I2["<b>Composant + schéma</b><br/><i>S1 · PDF airbag</i>"]
    K -->|section| I3["<b>Procédure</b><br/><i>remise à zéro</i>"]

    PIEGE["<i>« il fusibile dell'ABS si brucia sempre »</i><br/><b>contient un mot technique<br/>mais décrit une panne</b>"]
    PIEGE -.->|"doit aller ici"| D2

    classDef keep fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#111827
    classDef add fill:#dbeafe,stroke:#1e3a8a,stroke-width:2.5px,color:#111827
    classDef warn fill:#fef3c7,stroke:#d97706,stroke-width:2px,stroke-dasharray:5 3,color:#111827
    classDef dec fill:#f1f5f9,stroke:#475569,stroke-width:2px,color:#111827
    class DIAG,D1,D2,D3 keep
    class INFO,I1,I2,I3 add
    class PIEGE warn
    class Q,K dec
```

**Le piège en jaune est le cas à tester en priorité.** Il contient le mot « fusibile »
mais décrit un défaut : c'est exactement là qu'un modèle mal instruit se trompera et
dégradera une recherche qui marche aujourd'hui.

La nature de la réponse est décidée **en aval**, par le `kind` du chunk — pas par le
modèle. C'est ce qui permet de n'avoir qu'un seul outil.

---

## 6. Le drapeau de fonctionnalité — ton filet de sécurité

```mermaid
flowchart TD
    ENV{"<b>TECHNICAL_INFO_ENABLED</b>"}

    ENV -->|"false"| OFF["Outil <b>non déclaré</b> à Gemini<br/>Règle <b>non injectée</b> dans le prompt"]
    ENV -->|"true"| ON["Outil déclaré<br/>Règle injectée"]

    OFF --> R1["<b>Comportement identique<br/>à aujourd'hui</b><br/><small>au caractère près</small>"]
    ON --> R2["Diagnostic <b>+</b><br/>information technique"]

    R1 --> DEMO["<b>La démo fonctionne</b>"]
    R2 --> DEMO

    INC["<i>Le routage dérape<br/>la veille de la démo</i>"]
    INC -.->|"basculer le drapeau<br/>redémarrer un conteneur<br/><b>30 secondes</b>"| OFF

    classDef dec fill:#f1f5f9,stroke:#475569,stroke-width:2px,color:#111827
    classDef safe fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#111827
    classDef add fill:#dbeafe,stroke:#1e3a8a,stroke-width:2px,color:#111827
    classDef warn fill:#fef3c7,stroke:#d97706,stroke-width:2px,stroke-dasharray:5 3,color:#111827
    class ENV dec
    class OFF,R1,DEMO safe
    class ON,R2 add
    class INC warn
```

**À poser au jour 1, pas à la fin.** Ajouté à la fin, il ne protège de rien.

---

## 7. La feuille de route

Chaque jour se termine sur un état démontrable. Si le temps manque, on s'arrête au
dernier jour terminé — rien n'est laissé à moitié fait.

```mermaid
flowchart LR
    J1["<b>J1 · Extraction</b><br/>parseur + table<br/><small>risque 10 %</small>"]
    J2["<b>J2 · Indexation</b><br/>embeddings + endpoint<br/><small>risque 25 %</small>"]
    J3["<b>J3 · Routage</b><br/>outil + drapeau<br/><small>risque 40 %</small>"]
    J4["<b>J4 · Affichage</b><br/>carte + lien PDF<br/><small>risque 20 %</small>"]
    J5["<b>J5 · Scénarios</b><br/>script de démo<br/><small>risque 15 %</small>"]
    J67["<b>J6-J7</b><br/>marge<br/><small>sera consommée</small>"]

    J1 --> J2 --> J3 --> J4 --> J5 --> J67

    STOP{{"<b>POINT DE NON-RETOUR</b><br/>si la qualité des réponses<br/>n'est pas là, on s'arrête ici<br/>et on garde la démo actuelle"}}
    J2 -.-> STOP

    classDef low fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#111827
    classDef mid fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#111827
    classDef high fill:#fee2e2,stroke:#dc2626,stroke-width:2.5px,color:#111827
    classDef marge fill:#f1f5f9,stroke:#94a3b8,stroke-width:1.5px,stroke-dasharray:4 3,color:#475569
    class J1,J5 low
    class J2,J4 mid
    class J3 high
    class J67 marge
    class STOP high
```

---

## 8. Les deux risques — à ne pas confondre

```mermaid
flowchart TD
    subgraph RA["RISQUE A · ne pas tout finir"]
        A1["<b>≈ 60 %</b>"]
        A2["Cause : planning sans marge<br/>Conséquence : périmètre réduit"]
        A3["Scénario probable :<br/>J1-J3 faits, J4 partiel<br/><b>déjà mieux qu'aujourd'hui</b>"]
        A1 --- A2 --- A3
    end

    subgraph RB["RISQUE B · casser la démo"]
        B1["<b>≈ 10 %</b>"]
        B2["Bas car l'architecture est<br/><b>additive</b> : nouvelle table,<br/>nouveau module, nouvel endpoint"]
        B3["Couvert par :<br/>drapeau + tag demo-fi0396<br/>+ dump demo_fi0396.sql.gz"]
        B1 --- B2 --- B3
    end

    RA ~~~ RB

    classDef high fill:#fee2e2,stroke:#dc2626,stroke-width:2.5px,color:#111827
    classDef low fill:#dcfce7,stroke:#16a34a,stroke-width:2.5px,color:#111827
    classDef note fill:#ffffff,stroke:#94a3b8,stroke-width:1.5px,color:#111827
    class A1 high
    class B1 low
    class A2,A3,B2,B3 note
```

**C'est le risque B qui devrait guider ta décision, pas le A.** Ne pas tout finir coûte un
périmètre réduit. Casser la démo coûte la démo.

---

## 9. Vue d'ensemble — où tout se branche

```mermaid
flowchart TB
    U["<b>Mécanicien</b><br/>frontend Angular"]

    U --> CH["<b>Chat Service</b><br/>routage Gemini"]

    CH --> T1["FindCar"]
    CH --> T2["SearchByFaultCode"]
    CH --> T3["SearchBySymptom"]
    CH --> T4["SearchBySystem"]
    CH --> T5["<b>SearchTechnicalInfo</b><br/><small>NOUVEAU · derrière le drapeau</small>"]

    T1 --> VS["<b>Vehicle Service</b>"]
    T2 --> SS["<b>Search Service</b>"]
    T3 --> SS
    T4 --> SS
    T5 --> SS2["<b>Search Service</b><br/>/api/search/technical<br/><small>NOUVEL ENDPOINT</small>"]

    VS --> DB[("<b>PostgreSQL</b>")]
    SS --> DB
    SS2 --> DB

    DB --- TBL["gup_rows · documents<br/>graph_edges<br/>document_embeddings<br/>symptom_embeddings<br/><b>knowledge_chunks</b> ←"]

    SS2 -.->|"kind = legend"| FILES["<b>Route statique PDF</b><br/><small>NOUVEAU · 7 schémas</small>"]
    FILES --> U

    classDef keep fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#111827
    classDef add fill:#dbeafe,stroke:#1e3a8a,stroke-width:2.5px,color:#111827
    classDef data fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#111827
    class U,CH,T1,T2,T3,T4,VS,SS keep
    class T5,SS2,FILES add
    class DB,TBL data
```

Tout ce qui est en vert existe et continue de fonctionner à l'identique. Tout ce qui est
en bleu s'ajoute à côté, sans rien remplacer.
