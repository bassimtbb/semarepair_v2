# Architecture — Assistant documentaire technique (v2)

> Décision : développer avant la démo, pour montrer au client un produit qui dépasse la
> recherche de pannes. Ce document est la spécification d'exécution.
>
> Tous les chiffres viennent des données réelles de FI0396, mesurés et non estimés.
> Écrit pour toi ; la section « Argumentaire » est partageable avec ton patron.

---

## 1. Le cadre, dit franchement

**Contrainte** : la démo est dans 5 à 7 jours. Le périmètre complet représente 6 à 7 jours
de travail sans aléa. Il n'y a **aucune marge**.

Toute l'architecture ci-dessous est donc gouvernée par un principe unique : **rien de ce
qui fonctionne aujourd'hui ne doit pouvoir casser**. Chaque choix technique privilégie la
réversibilité sur l'élégance. Là où deux solutions se valent, je prends celle qui a le
moins de pièces mobiles, même si elle est moins belle.

Deux conséquences, à accepter dès maintenant :

- On livrera un **sous-ensemble** du périmètre décrit dans `Extension_Documents_Techniques.md`,
  pas sa totalité.
- La démo doit pouvoir se dérouler **avec ou sans** la nouveauté. C'est le rôle du
  drapeau de fonctionnalité en §6.

---

## 2. Ce que contiennent réellement les 64 documents

Quatre familles, mesurées document par document :

| Famille | Types | Docs | Contenu réel | Aujourd'hui |
| --- | --- | ---: | --- | --- |
| **A — Fiches de panne** | `GUP` | 36 | `XCAPITOLO` *Ordine* 1/3/4 | ✅ exploité |
| **B — Faits tabulaires** | `FUS` `COP` `DTM` `FAR` `LCZ` `SCH` | 12 | **226 faits** + **113 légendes** + **7 PDF** | ❌ titre seul |
| **C — Prose technique** | `DIS` `AZZ` `IND` `FLT` `NOT` `POP` `INF` `CIA` `INT` | 10 | **36 chapitres, 15 790 caractères** × 5 langues | ❌ titre seul |
| **D — Coquilles** | `CDD` `PTG` `LGR` `RCD` `RGR` | 6 | liens externes uniquement | ❌ sans valeur |

### La découverte qui change le plan

La famille **C n'était pas dans la proposition initiale**. Ces 10 documents contiennent de
la vraie prose technique — et ils sont perdus pour une raison triviale : `resx_parser.py`
ne lit que les chapitres d'`Ordine` 1, 3 et 4, alors que ceux-ci utilisent 2, 10, 20, 50,
55, 60…

Ce que ça représente concrètement :

```
AZZ 62506917  Ripristino Indicatore Assistenza   2 chap. 1 240 car.
              -> la procédure de remise à zéro de l'indicateur d'entretien
IND 62503109  Presa Diagnosi                     2 chap.   427 car.
              -> "Presa tipo OBD 16 Pin. Cruscotto - lato inferiore sinistro"
                 + le brochage complet des 16 broches
FLT 25019681  Filtro Abitacolo                   1 chap.   365 car.
              -> "Dimensioni: L=435 H=145 S=18 mm. Localizzazione: Vano..."
DIS 199244103 Disposizione Montaggio             11 chap. 2 890 car.
              -> avertissements et procédure de dépose des courroies
INT 199006282 Richiami Ufficiali                 9 chap. 5 901 car.
              -> les rappels constructeur, par période de production
```

**C'est la famille la moins chère à exploiter et la plus rentable** : c'est du texte libre,
exactement ce que l'infrastructure d'embeddings sait déjà traiter. Aucune structure
nouvelle à inventer — il suffit de cesser d'ignorer ces chapitres.

### Famille B, en détail

| Source | Structure | Volume |
| --- | --- | ---: |
| `FUS` Fusibili | `Numero` / `Descrizione` / `Riferimento`, dans 4 boîtiers | **122** |
| `DTM` Dati Meccanici | `Gruppo` / `Dato` / `Valore` / `UnitaMis` | 41 |
| `LCZ` Localizzazione | `Numero` / `NomeComp` / `Localizzazione` | 26 |
| `COP` Coppie di Serraggio | `Gruppo` / `Dato` / `Valore` / `UnitaMis` | 21 |
| `FAR` Fari e Lampadine | idem | 16 |
| `SCH` Légendes des schémas | `Numero` / `NomeComp` | **113** |

Deux formes seulement : le quadruplet `Gruppo/Dato/Valore/UnitaMis` et le triplet
`Numero/Descrizione/Riferimento`. **Deux parseurs, pas six.**

### Les fichiers joints

**7 PDF référencés, 7 fournis.** Correspondance exacte avec les 7 schémas `SCH`.

**55 images référencées, 0 fournie.** Photos des boîtiers à fusibles, emplacements de
composants, icônes. On pourra dire *« F04, 50 A, boîtier compartiment moteur »* mais pas
le montrer. À demander au patron, sans bloquer le développement.

---

## 3. Le choix d'architecture central

### Une seule table, pas trois

L'approche naturelle serait : une table pour les faits tabulaires, une pour les chapitres
de prose, une pour les légendes — chacune avec sa table d'embeddings. Six tables, trois
parseurs, trois chemins de recherche.

**Je prends le chemin inverse : une seule table, `knowledge_chunks`.** Un *chunk* est une
unité cherchable, quelle que soit son origine. Une colonne `kind` distingue les natures.

Pourquoi, alors que c'est moins « propre » sur le papier :

- **Un seul passage d'embeddings** au lieu de trois. Moins de code, moins de bugs, une
  seule reprise sur erreur à gérer.
- **Une seule requête de recherche.** Le classement par similarité se fait sur un corpus
  homogène ; avec trois tables il faudrait fusionner et rééquilibrer trois classements,
  ce qui est un problème en soi.
- **Une seule migration.** En cas d'échec, `DROP TABLE knowledge_chunks` et tout revient
  à l'état actuel. C'est la propriété qui compte le plus cette semaine.

Le coût de ce choix est réel et assumé : la table mélange des natures différentes, et
elle devra probablement être éclatée le jour où le produit grandira. C'est un choix de
calendrier, pas un choix d'architecte — et il est écrit ici pour que personne ne croie
plus tard que c'était de l'ignorance.

### Schéma

```sql
CREATE TABLE knowledge_chunks (
    id            SERIAL PRIMARY KEY,
    id_documento  TEXT NOT NULL,
    language      TEXT NOT NULL,
    kind          TEXT NOT NULL,   -- 'fact' | 'section' | 'legend'
    heading       TEXT,            -- Gruppo, Capitolo, ou titre du boîtier
    label         TEXT,            -- Dato, Descrizione, NomeComp
    value         TEXT,            -- Valore, Riferimento (ampérage), Localizzazione
    unit          TEXT,            -- UnitaMis
    reference     TEXT,            -- Numero : F04, H1, S1
    body          TEXT,            -- prose (famille C) ; NULL pour les faits
    search_text   TEXT NOT NULL,   -- ce qui est embarqué : heading + label + body
    embedding     vector(768),
    created_at    TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_chunks_doc  ON knowledge_chunks (id_documento, language);
CREATE INDEX idx_chunks_kind ON knowledge_chunks (kind, language);
```

`search_text` est calculé à l'ingestion et stocké : on sait ainsi exactement ce qui a été
embarqué, sans devoir le reconstruire pour comprendre un résultat. Même principe que
`document_embeddings.embed_text` aujourd'hui.

**Aucune table existante n'est modifiée.** `documents`, `gup_rows`, `graph_edges`,
`document_embeddings`, `symptom_embeddings` restent intactes.

### Volume attendu

```
famille B : 226 faits + 113 légendes = 339
famille C : 36 chapitres
                               par langue : ~375 chunks
            × 5 langues (couverture réelle 290/320) : ~1 700 chunks
            embeddings : ~1 700 appels ≈ 0,05 $, ~10 minutes
```

---

## 4. Relations entre documents : ce qu'on ajoute, et ce qu'on n'ajoute pas

Tu as demandé s'il fallait de nouvelles relations dans le graphe. **Réponse : non, aucune
en v1.** Voici le raisonnement, parce que c'est contre-intuitif.

### Les relations existantes suffisent

```
DOCUMENTED_IN       car → document      déjà créée pour les 64 documents
AFFECTS_SYSTEM      document → system   n'existe que pour les GUP
INVOLVES_DEVICE     document → device   idem
CONTAINS_FAULT      document → DTC      idem
HAS_TRANSLATION     document ↔ document
RELATED_TO          DTC ↔ DTC
SHARES_ENGINE_WITH  car ↔ car           0 lien sur ce jeu de données
```

`DOCUMENTED_IN` est déjà posée sur les 64 documents — **vérifié** sur `25053805`. Le
filtrage par véhicule fonctionne donc dès le premier jour, sans rien ajouter.

### Pourquoi j'écarte `ILLUSTRATED_BY` (fiche → schéma)

C'est la relation qu'on a envie de créer. Les données ne la soutiennent pas : **une seule
fiche sur 36** cite un composant rattachable à un schéma ou à une table. Le cas existe —
la fiche `199310118` mentionne *« Fusibile F17 »*, et la table donne `F17 = Centralina
Iniezione, 5 A, vano motore`, correspondance exacte — mais un cas sur trente-six ne
justifie pas une relation, une migration et un chemin de code.

### Ce qui remplace la relation

**La recherche vectorielle fait le même travail, sans structure.** Un mécanicien qui
cherche *« sensore impatto laterale »* trouvera la légende `S1` du schéma Airbag parce que
le texte correspond — pas parce qu'une arête l'a prévu. C'est plus robuste : ça fonctionne
pour des formulations que personne n'a anticipées.

Le lien fiche ↔ schéma viendra quand le client fournira `RifIDDocCMP`, aujourd'hui à `0`
dans l'export. **C'est une question à poser, pas un problème à contourner.**

---

## 5. Le routage

C'est le point le plus délicat du projet. L'état actuel :

```
prompt de routage : 12 147 caractères, 8 règles de premier niveau
outils            : FindCar, SearchByFaultCode, SearchBySymptom, SearchBySystem
```

### Un seul outil nouveau, pas trois

L'envie serait de créer `SearchTechnicalData`, `SearchSchema`, `SearchProcedure`. **Je
n'en crée qu'un seul :**

```
SearchTechnicalInfo(query, engineCode, brand)
```

Chaque outil ajouté multiplie les frontières de décision. Avec un seul, le modèle n'a
qu'**une distinction binaire** à faire : *panne* ou *information technique*. La nature de
la réponse — une valeur, un schéma, une procédure — est décidée **en aval**, par le `kind`
du chunk retourné, pas par le modèle.

C'est le choix qui réduit le plus le risque dans tout ce document.

### La règle de routage

À insérer dans `BuildRouting`, après la règle des DTC :

```
- Les outils de recherche ci-dessus servent à diagnostiquer un DÉFAUT : le mécanicien
  décrit un symptôme, un code, ou un système qui ne fonctionne pas.
  SearchTechnicalInfo sert à autre chose : le mécanicien demande une INFORMATION sur
  le véhicule, sans que rien ne soit en panne. Une valeur, un emplacement, une
  procédure d'entretien, un schéma électrique.
  Le test : est-ce que quelque chose ne va pas ? Si oui → diagnostic. Si le mécanicien
  veut simplement savoir → SearchTechnicalInfo.

    "quale fusibile protegge la centralina ABS"      → SearchTechnicalInfo
    "che coppia di serraggio per il coperchio"       → SearchTechnicalInfo
    "dove si trova la presa diagnosi"                → SearchTechnicalInfo
    "mostrami lo schema dell'airbag"                 → SearchTechnicalInfo
    "come azzero l'indicatore di servizio"           → SearchTechnicalInfo
    "che lampadina per l'anabbagliante"              → SearchTechnicalInfo

    "l'ABS non funziona"                             → SearchBySymptom
    "ho il codice P0380"                             → SearchByFaultCode
    "il fusibile dell'ABS si brucia sempre"          → SearchBySymptom  (un défaut,
                                                        même s'il nomme un fusible)
```

Le dernier exemple est le plus important : il contient le mot « fusibile » mais décrit une
panne. C'est exactement le cas où un modèle mal instruit se trompera.

### Règle de véhicule

`SearchTechnicalInfo` suit la **même contrainte que les autres** : pas de véhicule
confirmé, pas de réponse. Les valeurs techniques sont spécifiques au véhicule — un couple
de serrage donné pour le mauvais moteur est pire qu'une absence de réponse. On réutilise
le mécanisme existant (`BuildSearchUrl`, garde Rule 1) sans l'altérer.

### Formatage de la réponse

Le principe de fidélité ne change pas : **le modèle ne rédige jamais le contenu
technique**. Il reçoit les chunks en métadonnées et écrit une phrase d'accompagnement ;
la valeur, l'unité et la source sont rendues par le frontend depuis la base, comme les
fiches aujourd'hui.

---

## 6. Le garde-fou : un drapeau de fonctionnalité

**C'est la pièce la plus importante de ce document pour ta démo.**

Une variable d'environnement, `TECHNICAL_INFO_ENABLED`, lue au démarrage du Chat Service :

- **Désactivée** → l'outil n'est pas déclaré à Gemini, la règle de routage n'est pas
  injectée dans le prompt. Le système se comporte **exactement** comme aujourd'hui, au
  caractère près.
- **Activée** → l'outil et la règle apparaissent.

Ce que ça t'achète : si le routage se dégrade la veille de la démo, tu bascules le drapeau,
tu redémarres un conteneur, et tu retrouves en trente secondes le produit qui fonctionne.
Tu ne débugges pas sous pression et tu ne perds pas la démo.

Le drapeau doit être posé **au premier jour**, pas ajouté à la fin. Ajouté à la fin, il ne
protège de rien.

---

## 7. Feuille de route

Séquencée pour que **chaque jour se termine sur un état démontrable**, du plus sûr au plus
risqué. Si le temps manque, on s'arrête au dernier jour terminé — sans rien laisser à
moitié fait.

### Jour 1 — Extraction (aucun risque pour l'existant)

Un nouveau module `technical_parser.py`, à côté de `resx_parser.py` qui n'est pas touché.
Deux fonctions : le quadruplet `Gruppo/Dato/Valore/UnitaMis`, le triplet
`Numero/Descrizione/Riferimento`. Plus l'extraction des chapitres de famille C, quel que
soit leur `Ordine`.

Table `knowledge_chunks` créée par une nouvelle `schema.sql` additive.

**Livrable vérifiable** : ~1 700 chunks en base, comptés par type et par langue. Rien
d'autre n'a changé ; le chatbot fonctionne à l'identique.

### Jour 2 — Indexation et recherche brute

Embeddings sur `search_text`, en réutilisant `embedder.py` tel quel. Puis un endpoint
`GET /api/search/technical` dans Search Service, sur le modèle exact de `SymptomWithCarAsync` :
filtrage par véhicule via `gup_rows`, classement vectoriel, seuil de pertinence.

**Livrable vérifiable** : des requêtes `curl` qui renvoient les bons faits, sans aucun LLM.
C'est le jour où l'on saura si la qualité est au rendez-vous — et c'est **le point de
non-retour** : si les réponses sont mauvaises ici, on s'arrête et on garde la démo
actuelle.

### Jour 3 — Routage, derrière le drapeau

Déclaration de l'outil, règle de prompt, drapeau `TECHNICAL_INFO_ENABLED`, câblage dans
`RepairOrchestrator`.

**Livrable vérifiable** : la batterie de scénarios existants rejouée **drapeau activé**,
pour confirmer qu'aucune recherche de panne n'a été détournée. C'est le test qui compte,
pas celui des nouvelles questions.

### Jour 4 — Affichage

Une carte compacte pour une valeur : libellé, valeur, unité, source. Composant Angular
neuf, sur le modèle de `case-summary-card`.

Pour les schémas : **un bouton « Apri schema (PDF) »**, pas un visualiseur intégré. Une
route statique qui sert le fichier, un lien qui l'ouvre dans un onglet. 90 % de l'effet
pour 10 % du risque. Le visualiseur intégré est explicitement **hors périmètre**.

### Jour 5 — Scénarios et script de démo

Rejouer chaque question en réel, choisir celles qui fonctionnent, reconstruire
`Demo_Script_Client.md` — qui est de toute façon à refaire depuis la réduction à FI0396.

### Jours 6–7 — Marge

Volontairement vides. Ils seront consommés : ils le sont toujours.

---

## 8. Les risques, chiffrés

Tu as demandé un pourcentage. Deux questions différentes se cachent derrière, et elles
n'ont pas du tout la même réponse.

### Risque A — ne pas finir le périmètre complet à temps

| Étape | Charge | Probabilité de dérapage |
| --- | --- | ---: |
| J1 Extraction | 1 j | 10 % |
| J2 Indexation + recherche | 1 j | **25 %** — qualité des réponses inconnue |
| J3 Routage | 1–2 j | **40 %** — le vrai point dur |
| J4 Affichage | 1 j | 20 % |
| J5 Scénarios | 1 j | 15 % |

**Probabilité de livrer les cinq étapes dans les temps : environ 40 %.**
Autrement dit **~60 % de risque** de ne pas tout avoir. C'est élevé, et c'est la
conséquence directe d'un planning sans marge — pas d'une difficulté technique particulière.

### Risque B — casser la démo qui fonctionne

**Environ 10 %**, et c'est le seul chiffre qui devrait guider ta décision.

Il est bas parce que l'architecture est additive : nouvelle table, nouveau module, nouvel
endpoint, nouveau composant. Le seul point de contact avec l'existant est le prompt de
routage — et le drapeau l'annule.

Les 10 % résiduels couvrent ce qu'un drapeau ne protège pas : une erreur dans la migration
de schéma, une ingestion qui abîme la base, une manipulation git malheureuse. **Le dump
`demo_fi0396.sql.gz` et le tag `demo-fi0396` couvrent exactement ces cas** — c'est pour ça
qu'on les a faits hier.

### Le scénario le plus probable

Ni le succès complet ni l'échec : **J1 à J3 terminés, J4 partiellement**. Tu montres des
réponses techniques réelles en conversation, avec le lien PDF comme cerise si le temps l'a
permis. C'est déjà très supérieur à la démo actuelle, et c'est ce que je viserais.

### Comment faire tomber le risque A

Si tu veux du quasi-certain, **coupe la famille B et les schémas, garde la famille C**.
10 documents, 36 chapitres, du texte libre qui passe par le chemin d'embeddings existant :
c'est **1 jour et demi** au lieu de 5, et le risque tombe sous les 15 %. Tu perds les
fusibles et les schémas — donc l'effet « waouh » — mais tu gagnes la certitude de montrer
quelque chose de neuf.

---

## 9. Ce qu'on ne fait pas, et pourquoi

Écrit noir sur blanc pour que ces sujets ne reviennent pas par la fenêtre en cours de
route :

- **Pas de visualiseur PDF intégré.** Un lien qui ouvre un onglet suffit à la démonstration.
- **Pas de nouvelle relation de graphe.** Les données ne les soutiennent pas (§4).
- **Pas de famille D.** Ces 6 documents ne contiennent que des liens externes.
- **Pas de reprise des 55 images manquantes.** On ne les a pas ; à demander au patron.
- **Pas de refonte du parseur existant.** `resx_parser.py` n'est pas touché : il fait
  tourner la démo.
- **Pas de tentative de lier automatiquement fiches et schémas.** Un cas sur 36.

---

## 10. Argumentaire pour ton patron

*Partageable tel quel.*

L'archive du client contient bien plus que des fiches de panne. Sur un seul véhicule, les
documents déjà livrés recèlent **122 fusibles avec leur fonction et leur ampérage**,
**41 données moteur**, **21 couples de serrage**, **16 références d'ampoules**,
**26 emplacements de calculateurs**, **7 schémas électriques** couvrant ABS, airbag,
antidémarrage et climatisation — et **36 chapitres de procédures techniques**.

Aucune de ces informations n'est exploitée aujourd'hui, et **aucune des 36 fiches de panne
ne les contient**. Ce sont les questions qu'un mécanicien pose le plus souvent et
auxquelles le produit actuel ne sait pas répondre : *quel fusible, quel couple, quelle
ampoule, où se trouve ce composant, comment remettre à zéro l'indicateur d'entretien*.

Le message au client n'est plus *« nous cherchons dans vos documents »* mais **« nous
savons exploiter tout votre patrimoine documentaire, y compris ce que vous n'aviez jamais
rendu interrogeable »**.

Ce qu'il faut lui demander en retour, et qui ne coûte rien : les **55 images** référencées
mais non fournies, et le lien fiche ↔ schéma (`RifIDDocCMP`), présent dans leur système
mais vide dans l'export.
