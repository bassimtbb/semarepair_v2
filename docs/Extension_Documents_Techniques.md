# Extension — exploiter les documents techniques, pas seulement les fiches de panne

> Proposition. Écrit pour toi, partageable avec ton patron.
> Tous les chiffres et exemples viennent des données réelles de FI0396, vérifiés.

## L'idée en une phrase

Aujourd'hui le mécanicien décrit une panne et reçoit une fiche de réparation. Demain il
pose **n'importe quelle question technique sur son véhicule** — un fusible, un couple de
serrage, une ampoule, un schéma de câblage — et reçoit la réponse exacte, extraite de la
même archive.

## Pourquoi c'est le bon moment

Sur les 64 documents de FI0396, **36 sont des fiches de panne** et alimentent le produit
actuel. Les **28 autres** sont déjà là, déjà livrés, et ne servent à rien : le parseur n'en
tire que le titre. Or ils contiennent, une fois comptés :

| Source | Contenu | Volume |
| --- | --- | --- |
| `FUS` Fusibili e Relè | numéro, fonction protégée, ampérage, boîtier | **122 lignes** |
| `DTM` Dati Meccanici | cylindrée, lubrifiants, capacités | **41 valeurs** |
| `LCZ` Localizzazione | emplacement des calculateurs | **26 entrées** |
| `COP` Coppie di Serraggio | couple en Nm par pièce | **21 valeurs** |
| `FAR` Fari e Lampadine | type d'ampoule, volts, watts | **16 valeurs** |
| `SCH` Schemi Elettrici | 7 schémas + **7 PDF fournis** + légendes nommées | ~160 composants |

Soit **226 faits techniques** et **7 schémas consultables**, pour un seul véhicule. Le tout
déjà sur disque, sans rien demander de plus au client.

## Les trois réponses qu'on ne sait pas donner aujourd'hui

### 1. Une valeur technique, directement

Pas un document : **la réponse**, avec sa source.

> *« Quale fusibile protegge la centralina ABS? »*
> → **F04 · 50 A · Scatola Fusibili - Vano Motore**

> *« Che coppia di serraggio per il coperchio punterie? »*
> → **10 Nm**

> *« Che lampadina per l'anabbagliante? »*
> → **H7 · 12 V / 55 W**

C'est un changement de nature : le produit passe de « je te trouve un document » à
« je te donne le chiffre ». C'est ce qu'un mécanicien demande le plus souvent, et c'est
justement ce qu'aucune fiche de panne ne contient.

### 2. Un schéma électrique

> *« Mostrami lo schema dell'airbag »*
> → le PDF `62502268`, affiché dans la conversation

Les 7 PDF livrés couvrent ABS Bosch 5.3 (4 variantes), Airbag Siemens MY99,
antidémarrage (Code) et climatiseur automatique — des systèmes qu'**aucune des 36 fiches
ne traite**.

### 3. Un composant situé dans le circuit

C'est la combinaison la plus forte, et elle marche parce que les deux moitiés s'emboîtent.
Le PDF montre des repères (`H1`, `B4`, `S1`, `F01`) ; la légende du `.resx` les nomme :

```
H1  = Centralina Airbag
B4  = Airbag laterale anteriore sinistro
S1  = Sensore Impatto laterale sinistro
F01 = Fusibile 7,5A
```

> *« Dove si trova il sensore d'impatto laterale sinistro? »*
> → **S1 sur le schéma Airbag Siemens MY99** + le schéma affiché

Le mécanicien cherche avec ses mots ; le système traduit en repère et lui montre le dessin.

## Pourquoi c'est faisable sans tout reconstruire

Trois briques existent déjà et se réutilisent telles quelles :

- **La recherche par le sens.** `symptom_embeddings` indexe déjà le texte des pannes.
  Indexer `Gruppo + Dato` des tables techniques suit exactement le même chemin —
  `embedder.py` n'a pas besoin d'être repensé, juste appelé sur une nouvelle source.
- **Le routage par outils.** Gemini choisit déjà entre `SearchByFaultCode`,
  `SearchBySymptom`, `SearchBySystem` et `FindCar`. Ajouter `SearchTechnicalData` et
  `SearchSchema` est un ajout, pas une refonte.
- **Le filtrage par véhicule.** `gup_rows` relie déjà chaque document au véhicule. Les
  documents techniques y sont **déjà** : ils remonteront filtrés sur le bon véhicule dès
  le premier jour.

## Ce qu'il faut construire, par ordre

### Étape 1 — Extraire les faits techniques

Les quatre documents tabulaires partagent une structure identique et propre :
`Gruppo / Dato / Valore / UnitaMis`. Les fusibles suivent leur propre forme —
`Numero / Descrizione / Riferimento` imbriqués dans un boîtier — mais tout aussi régulière.

Une nouvelle table `technical_facts` (document, langue, groupe, libellé, valeur, unité,
contexte) et un parseur dédié. Le parseur actuel n'est pas touché : il continue de lire
les `XCAPITOLO` des fiches GUP, on ajoute une seconde voie pour les autres types.

### Étape 2 — Rendre ces faits cherchables

Un embedding par fait, sur `Gruppo + Dato`, avec la même mécanique que les symptômes.
Ordre de grandeur : 226 faits × 5 langues ≈ 1 100 embeddings pour ce véhicule, soit
quelques centimes.

### Étape 3 — Un nouvel outil de recherche

`SearchTechnicalData(query, engineCode, brand)` côté Search Service, et la déclaration
correspondante côté Chat. Le travail délicat n'est pas le code mais **la règle de
routage** : apprendre au modèle à distinguer *« ne démarre pas »* (une panne) de
*« quel fusible »* (une valeur). C'est le point à tester le plus sérieusement, parce
qu'une mauvaise règle peut dégrader les recherches qui marchent aujourd'hui.

### Étape 4 — Afficher

Deux nouveaux rendus dans la conversation :

- **Une carte compacte pour une valeur** — libellé, valeur, unité, et la source
  (« Fusibili e Relè · Vano Motore »). La transparence sur l'origine reste la règle.
- **Un visualiseur PDF** pour les schémas. C'est le seul vrai développement frontend :
  rien de tel n'existe aujourd'hui. Servir les PDF demande aussi une route HTTP, qui
  n'existe pas non plus.

### Étape 5 — Relier aux fiches existantes

Quand une fiche cite un composant présent dans une table ou une légende, proposer le
complément. Le cas existe déjà : la fiche `199310118` cite *« Fusibile F17 »*, et la table
donne `F17 = Centralina Iniezione, 5 A, vano motore` — correspondance exacte.

**À garder pour la fin, et sans en attendre trop** : sur les 36 fiches, **une seule**
mentionne un fusible. Le gisement est dans les questions nouvelles (étapes 1 à 4), pas
dans l'enrichissement des fiches.

## Ce qui manque, et qu'il faut demander

**55 images référencées, 0 fournie.** Les 7 PDF de schémas sont complets, mais toutes les
autres illustrations manquent : photos des boîtiers à fusibles, emplacements de
composants, icônes. Concrètement, on pourra dire *« F04, 50 A, boîtier compartiment
moteur »* mais pas **montrer** le boîtier. À demander au patron — même mécanisme
`GETFILE:` que les PDF, donc probablement la même facilité d'export.

**Les schémas interactifs** (`RifIDFileIMGANI`) ne sont pas livrés non plus, seulement
les PDF statiques. Suffisant pour commencer.

**Une question de droits.** Les schémas portent la mention *« WDB World Data Bank — All
rights reserved »*. Du contenu tiers : à valider avant de l'afficher à un client.

## Les risques, dits franchement

**Le routage est le vrai risque.** Ajouter deux outils à un modèle qui en a déjà quatre
augmente les chances qu'il choisisse mal. Le garde-fou existant — ne jamais inventer de
contenu technique — reste intact, mais une question de panne pourrait partir vers la
recherche de valeurs et ne rien trouver. À tester scénario par scénario, comme pour la
démo.

**Le volume reste modeste.** 226 faits pour un véhicule, c'est peu pour juger. L'archive
complète du client dira si cette voie tient à l'échelle ou si elle reste un complément.

**Ne pas confondre les deux usages.** « Trouve-moi une panne » et « donne-moi une valeur »
sont deux produits dans la même interface. Si les réponses se mélangent, le mécanicien
perd confiance dans les deux.

## Phasage proposé

**Avant la démo : rien.** La démo actuelle fonctionne, le script est à reconstruire, et ce
chantier n'est pas mince. Les PDF sont en sécurité dans le dépôt, ils attendront.

**Juste après :** les étapes 1 et 2 — extraire et indexer. C'est du travail de données,
sans risque pour l'existant, et ça permet de mesurer la qualité des réponses avant
d'investir dans l'interface.

**Ensuite :** les étapes 3 et 4, avec une campagne de tests de routage aussi sérieuse que
celle de la démo.

**L'argument commercial, lui, est disponible tout de suite** : *« votre archive ne
contient pas que des fiches de panne, et nous savons tout exploiter »*. Ça se dit avec les
chiffres de ce document, sans attendre une ligne de code.
