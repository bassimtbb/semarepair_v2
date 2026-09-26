# Démo client — SemaRepair

Français pour toi. **Italien pour tout ce que tu tapes et tout ce que tu dis.**

Chaque scénario se lit dans l'ordre : je tape → **je dis un paragraphe** → le résultat
s'affiche → **je dis un deuxième paragraphe**.

Tout a été rejoué en réel contre la stack avant écriture. Les sorties décrites sont celles
observées, pas des suppositions.

---

## Avant de commencer

```bash
docker compose up -d
```

Ouvrir **`http://localhost/`** dans **Chrome**, zoom **110 %**, thème clair.

**Chrome obligatoirement.** Le schéma électrique s'affiche dans un cadre PDF que Chrome,
Edge et Firefox savent rendre — pas Safari sur iPhone, qui montrerait un cadre blanc.

Vérifier que l'extension est active :

```bash
docker exec semarepair_v2-chat-service-1 sh -c 'echo $TECHNICAL_INFO_ENABLED'   # doit dire true
```

Débloquer la dashboard maintenant : clic sur le bouton grille, coller
`USAGE_DASHBOARD_KEY` du `.env`. Sinon elle te la demandera devant le client.

**F5 entre chaque scénario.** Sans ça, le véhicule du scénario précédent reste en mémoire.

---

## 1. Le véhicule, puis un code défaut

### Je tape

```
Sto lavorando su un Fiat Ducato 2.8 JTD del 2004
```

Puis, après avoir cliqué la carte :

```
Ho il codice errore P0380
```

### Avant le résultat

> Il meccanico parte sempre dalla macchina che ha sul ponte. Poi collega la diagnosi e
> legge un codice errore. Scrive solo quello.

### Ce qui s'affiche

Une carte `FIAT Ducato · 2.8 JTD 8v · 8140.43S`, la pastille véhicule en haut, puis
**4 fiches** en `Iniezione` : raccord de pompe, résistance de reniflard, **fusible F17**,
bougie de préchauffage.

### Après le résultat

> Il veicolo è confermato, lo vedete qui in alto. Da adesso ogni ricerca è filtrata su
> questo motore. E con un solo codice ho già quattro schede di riparazione, ognuna con il
> suo grado di attendibilità.

⏱ **2 min 30**

---

## 2. Le symptôme, avec les mots du mécanicien

### Je tape

```
Il motore non si avvia dopo un arresto in marcia
```

### Avant le résultat

> Adesso senza codice. Solo quello che il meccanico vede e che il cliente gli racconta,
> in italiano normale.

### Ce qui s'affiche

**3 fiches.** La première : *Mancato avviamento motore, a seguito di un arresto in marcia*
— cause : **Fusibile F17 (Protezione centralina iniezione)**.

### Après le résultat

> Ha capito il sintomo senza nessun codice. E guardate la causa della prima scheda: il
> fusibile F17.

**Ouvre cette fiche** et laisse « Fusibile F17 » à l'écran — le scénario suivant part de là.

⏱ **2 min**

---

## 3. ⭐ L'enchaînement — le moment clé

### Je tape

```
Dove si trova il fusibile F17 e che amperaggio ha?
```

### Avant le résultat

> La scheda mi dice che il colpevole è il fusibile F17. Ma dove si trova? E che
> amperaggio? La scheda non lo dice — nessuna scheda di riparazione lo dice.

### Ce qui s'affiche

**F17 · Centralina Iniezione · 5 (A)**, avec sa source : *Fusibili e Relè · Scatola
Fusibili - Vano Motore*. Et en dessous les fusibles voisins, F16 et F18.

### Après le résultat

> Ecco il punto. Fino a un minuto fa vi ho mostrato un assistente che trova schede di
> guasto. Adesso vi sto mostrando qualcosa d'altro: risponde a una domanda tecnica
> qualsiasi sul veicolo. Amperaggio, posizione, coppia di serraggio, lampadina.
> Sono le domande che un meccanico fa dieci volte al giorno, e a cui nessuna scheda di
> riparazione risponde. Queste informazioni erano già nel vostro archivio. Nessuno le
> aveva mai rese consultabili.

⏱ **3 min** — ralentis ici, c'est l'argument qui justifie l'investissement.

---

## 4. Le schéma électrique, dans la conversation

### Je tape

```
Mostrami lo schema elettrico dell'airbag
```

### Avant le résultat

> E quando l'informazione non è un numero ma un disegno?

### Ce qui s'affiche

Le **schéma airbag s'affiche directement dans la conversation**, avec sa légende
`H1 · Centralina Airbag`. En dessous, le fusible `F50 · Centralina Airbag · 7,5 (A)`.

**Clique l'icône plein écran** (en haut à droite du schéma).

### Après le résultat

> Il disegno è qui, nella conversazione, e con un clic riempie lo schermo. I riferimenti
> sul disegno — H1, B1, S1 — hanno un nome: H1 è la centralina airbag, S1 il sensore
> d'impatto laterale sinistro. Il meccanico cerca con parole sue, il sistema gli trova il
> disegno giusto.

> Se il PDF resta bianco : tu n'es pas dans Chrome. Utilise le bouton « ouvrir dans un
> onglet », à gauche du plein écran.

⏱ **2 min 30**

---

## 5. Une valeur technique

### Je tape

```
Che coppia di serraggio per il coperchio delle punterie?
```

### Avant le résultat

> Un'ultima domanda di officina, di quelle banali.

### Ce qui s'affiche

**Coperchio delle punterie · 10 Nm**, puis *Cappellotti albero a camme · 18 Nm* et
*Testata 3° fase · 180°*.

### Après le résultat

> Dieci newton metro. Non una scheda da leggere, il numero. E accanto, gli altri
> serraggi dello stesso gruppo, perché chi apre un coperchio punterie avrà bisogno anche
> di quelli.

⏱ **1 min 30**

---

## 6. Multilingue

⚠️ **En français.** L'espagnol n'est pas détecté de façon fiable sur ces phrases.

### Je tape

```
Je travaille sur un Fiat Ducato 2.8 JTD
```
puis, après avoir cliqué la carte :
```
Quel fusible protège le calculateur ABS ?
```

### Avant le résultat

> Stesso prodotto, ma il meccanico scrive in francese.

### Ce qui s'affiche

> Voici les informations trouvées concernant le fusible du calculateur ABS.

**F42 · Boîtier électronique ABS · 7,5 (A)** et **F04 · Boîtier électronique ABS · 50 (A)**.

### Après le résultat

> Non ho premuto nessun pulsante. Riconosce la lingua da solo e risponde in quella —
> italiano, francese, inglese, spagnolo e portoghese. E notate: due fusibili proteggono
> la centralina ABS, uno in ogni scatola. Ve li dà tutti e due, non ne sceglie uno.

⏱ **1 min 30**

---

## 7. Il n'invente jamais

### Je tape

Les trois à la suite, sans réinitialiser.

```
Il fusibile dell'ABS si brucia sempre
```
```
Che tempo fa a Milano oggi?
```
```
Sto lavorando su una Tesla Model 3
```

### Avant le résultat

> Ultima parte, quella che conta per la vostra responsabilità. Tre domande fatte apposta
> per metterlo in difficoltà. La prima è la più interessante.

### Ce qui s'affiche

La première rend des **fiches de réparation**, pas un ampérage. Puis un refus poli. Puis
*« Non abbiamo un Tesla Model 3 a catalogo. »*

### Après le résultat

> La prima domanda nomina un fusibile, ma non chiede un amperaggio: dice che si brucia.
> È un guasto. E infatti mi ha dato le schede di riparazione, non il numero del fusibile.
> Capisce la differenza tra chi vuole sapere e chi ha un problema.
>
> Poi: se la domanda non c'entra, non ci prova. Se il veicolo non è in archivio, lo dice
> in una riga. Non inventa mai. Ogni frase tecnica esce dal vostro archivio, parola per
> parola.

⏱ **2 min**

---

## Plan B

| Problème | Je fais | Je dis |
| --- | --- | --- |
| Réponse lente | Attendre, ne pas recliquer | *Sta interrogando il modello e poi l'archivio.* |
| Mauvais résultat | **F5**, refaire | *Mi porto dietro la conversazione precedente. Riparto pulito.* |
| Le schéma reste blanc | Bouton « ouvrir dans un onglet » | *Ve lo apro a parte.* |
| Une question technique ne trouve rien | Reformuler avec le mot du métier | *Provo con il termine tecnico.* |
| Un service est tombé | `docker compose restart chat-service` puis F5 | *Un attimo, riavvio un servizio.* |
| L'extension déraille | `TECHNICAL_INFO_ENABLED=false` dans `.env`, `docker compose up -d chat-service` | *(30 secondes, retour au produit de base qui fonctionne)* |

**Fais des captures de chaque scénario la veille**, dans un onglet ouvert.

---

## Questions du client

**Quanto costa?**
> Frazioni di centesimo a domanda, misurato nella dashboard, non stimato. Indicizzare
> tutta la documentazione tecnica di un veicolo è costato meno di un centesimo.

**Queste informazioni da dove vengono?**
> Dal vostro archivio, quello che ci avete già dato. Su questo veicolo: 122 fusibili con
> la loro funzione e il loro amperaggio, 21 coppie di serraggio, 16 lampadine, 41 dati
> motore, 7 schemi elettrici e 36 capitoli di procedure. Nessuna di queste informazioni
> era consultabile prima.

**Funziona con tutto il nostro archivio?**
> Quello che vedete è un veicolo solo, caricato in pochi minuti. Il procedimento è
> automatico: ci date l'archivio completo e funziona allo stesso modo.

**Le informazioni le inventa?**
> No. L'intelligenza artificiale capisce la domanda e sceglie dove cercare. Il contenuto
> tecnico — il numero, la procedura, il disegno — viene dal database e arriva sullo
> schermo senza passare dal modello.

**E quando non c'è la risposta?**
> Ve lo dice. L'avete visto con la Tesla e con la domanda sul meteo.

**Quante lingue?**
> Cinque: italiano, francese, inglese, spagnolo e portoghese.

---

## Minutage

| # | Scénario | Durée |
| --- | --- | --- |
| — | Intro | 1 min |
| 1 | Véhicule + code défaut | 2 min 30 |
| 2 | Symptôme | 2 min |
| 3 | **L'enchaînement F17** | **3 min** |
| 4 | Schéma électrique | 2 min 30 |
| 5 | Couple de serrage | 1 min 30 |
| 6 | Multilingue | 1 min 30 |
| 7 | Garde-fous | 2 min |
| | **Total** | **≈ 16 min** |

Pour tenir 15 minutes, coupe le scénario 5 : le 3 a déjà montré qu'il donne des valeurs.
**Ne coupe jamais le 3 ni le 7.**

---

## Ce que tu ne dois PAS promettre

Le client demandera « et si je veux X ? ». Ces limites sont réelles :

- **Les schémas n'existent qu'en italien et en anglais.** Les fusibles, couples, ampoules
  et procédures sont dans les cinq langues, pas les 7 schémas.
- **Les images manquent.** 55 illustrations sont référencées mais n'ont pas été livrées :
  photos des boîtiers à fusibles, emplacements de composants. On dit *« F04, 50 A, boîtier
  compartiment moteur »*, on ne le montre pas.
- **Un seul véhicule en base.** C'est un choix, pas une limite technique — mais ne laisse
  pas croire que le catalogue complet est déjà chargé.
- **Le repli moteur partagé ne fonctionne pas sur ce jeu de données.** Il faut deux
  véhicules partageant un moteur ; il n'y en a qu'un. Ne le mentionne pas.

---

## Si on te demande comment ça marche

À l'import, chaque document est relié à ses véhicules, ses codes défaut et ses composants.
Les fiches de réparation d'un côté, et de l'autre tout ce qui n'en est pas — tables de
fusibles, couples, schémas, procédures — découpé en réponses consultables.

La recherche se fait par le sens et pas par les mots : « non si avvia dopo un arresto »
retrouve un document qui dit autre chose mais parle du même problème.

L'intelligence artificielle comprend la question et décide où chercher — une panne ou une
information. Elle ne rédige jamais le contenu technique : le numéro, la valeur et le
schéma sortent du document tel quel.
