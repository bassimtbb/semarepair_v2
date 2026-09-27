# Démo client — SemaRepair

Français pour toi. **Italien pour tout ce que tu tapes et tout ce que tu dis.**

Une seule histoire du début à la fin : **un Ducato qui ne démarre plus.** Chaque question
naît de la réponse précédente — ne saute aucune étape, c'est l'enchaînement qui convainc,
pas les fonctionnalités prises une à une.

Tout a été rejoué en réel sur la stack. Les sorties décrites sont celles observées.

---

## Avant de commencer

```bash
docker compose up -d
```

**Chrome obligatoirement** sur `http://localhost/`, zoom 110 %, thème clair.
Le schéma s'affiche dans un cadre PDF que Safari sur iPhone ne sait pas rendre.

```bash
docker exec semarepair_v2-chat-service-1 sh -c 'echo $TECHNICAL_INFO_ENABLED'   # doit dire true
```

Débloque la dashboard maintenant (bouton grille → colle `USAGE_DASHBOARD_KEY` du `.env`),
autorise le micro dans Chrome, et **F5 juste avant de commencer**.

---

## Acte 1 — La voiture est sur le pont

### Je tape

```
Sto lavorando su un Fiat Ducato 2.8 JTD del 2004
```

Puis je clique la carte du véhicule.

### Je dis

> Ogni riparazione comincia così: il meccanico ha una macchina davanti. La descrive con
> parole sue, non con un codice interno.

### Ce qui s'affiche

Une carte `FIAT Ducato · 2.8 JTD 8v · 8140.43S`, puis la **pastille véhicule** en haut,
avec années, alimentation, puissance et code moteur.

### Puis je dis

> Da adesso in poi tutto è filtrato su questo motore. Non dovrò più ripeterlo.

⏱ **1 min 30**

---

## Acte 2 — Le symptôme, avec ses mots à lui

### Je tape

```
Il motore non si avvia dopo un arresto in marcia
```

### Je dis

> Nessun codice, nessun termine tecnico. Solo quello che il meccanico vede e che il
> cliente gli racconta.

### Ce qui s'affiche

**3 fiches.** La première : *Mancato avviamento motore, a seguito di un arresto in
marcia*. **Ouvre-la** et laisse-la à l'écran.

Montre du doigt la ligne `Causa` : **Fusibile F17 (Protezione centralina iniezione)**.

### Puis je dis

> Ha capito il sintomo senza nessun codice. E guardate cosa dice la causa: il fusibile
> F17.

⏱ **2 min 30**

---

## Acte 3 — ⭐ La question que la fiche ne peut pas résoudre

C'est le cœur de la démonstration. Ralentis.

### Je tape

```
Dove si trova il fusibile F17 e che amperaggio ha?
```

### Je dis

> La scheda mi dice il colpevole. Ma dov'è questo fusibile? E che amperaggio? La scheda
> non lo dice — **nessuna scheda di riparazione lo dice.**

### Ce qui s'affiche

Une seule réponse, nette :

> **F17 · Centralina Iniezione**
> **5 (A)**
> Scatola Fusibili - Vano Motore

### Puis je dis

> Fino a un minuto fa vi ho mostrato un assistente che trova schede di guasto. Adesso vi
> sto mostrando qualcosa d'altro: risponde a qualsiasi domanda tecnica sul veicolo.
> Amperaggio, posizione, coppia di serraggio, lampadina.
>
> Sono le domande che un meccanico fa dieci volte al giorno. Queste informazioni erano
> **già nel vostro archivio** — nessuno le aveva mai rese consultabili.

⏱ **3 min**

---

## Acte 4 — Le schéma, dans la conversation

### Je tape

```
Mostrami lo schema elettrico dell'airbag
```

### Je dis

> E quando l'informazione non è un numero, ma un disegno?

### Ce qui s'affiche

Le **schéma airbag apparaît dans la conversation**, avec sa légende `H1 · Centralina
Airbag`. **Clique l'icône plein écran**, en haut à droite du schéma.

### Puis je dis

> Il disegno è qui, e con un clic riempie lo schermo. I riferimenti — H1, B1, S1 — hanno
> un nome: H1 è la centralina airbag, S1 il sensore d'impatto laterale sinistro. Il
> meccanico cerca con parole sue, il sistema gli trova il disegno giusto.

> Si le cadre reste blanc : tu n'es pas dans Chrome. Utilise le bouton « ouvrir dans un
> onglet », à gauche du plein écran.

⏱ **2 min 30**

---

## Acte 5 — Les mains sales, sous la voiture

L'enchaînement le plus naturel de toute la démo : il vient de regarder un schéma, il va
maintenant travailler dessus.

### Je fais

Je clique le **bouton du milieu** 〰️ dans la barre de saisie. À partir d'ici, **je ne
tape plus rien** — écrire couperait le mode vocal.

### Je dis

> Adesso il meccanico è sotto la macchina, con le mani sporche. Non tocca più niente.

### Je dis, à voix haute, au micro

> *Come azzero l'indicatore*

### Ce que le client entend

> « Ho trovato la procedura Regolazione - Reset. **Vuoi che te la legga?** »

### Je réponds, à voix haute

> *Sì*

Il lit alors **les huit étapes en entier**.

### Puis je dis

> Notate una cosa: non ha cominciato a leggere da solo. Una procedura è un minuto di
> parlato — ve lo chiede prima. Ma se chiedo un amperaggio, ve lo dice subito, senza
> domande: chiedere il permesso costerebbe più della risposta.

⏱ **3 min**

---

## Acte 6 — Il n'invente jamais

Reste en mode vocal, ou reviens au clavier — les deux marchent.

### Je tape

```
Il fusibile dell'ABS si brucia sempre
```
```
Sto lavorando su una Tesla Model 3
```

### Je dis

> Ultima parte, quella che conta per la vostra responsabilità. Due domande fatte apposta
> per metterlo in difficoltà.

### Ce qui s'affiche

La première rend des **fiches de réparation**, pas un ampérage. La seconde :
*« Non abbiamo un Tesla Model 3 a catalogo. »*

### Puis je dis

> La prima nomina un fusibile, ma non chiede un amperaggio: dice che **si brucia**. È un
> guasto. E infatti mi ha dato le schede di riparazione. Capisce la differenza tra chi
> vuole sapere e chi ha un problema.
>
> La seconda: se il veicolo non è in archivio, lo dice in una riga. Non inventa mai. Ogni
> frase tecnica esce dal vostro archivio, parola per parola. Preferiamo un « non lo so »
> a una riparazione sbagliata.

⏱ **2 min**

---

## Minutage

| Acte | Durée |
| --- | --- |
| 1 · La voiture sur le pont | 1 min 30 |
| 2 · Le symptôme | 2 min 30 |
| 3 · **Le fusible F17** | **3 min** |
| 4 · Le schéma | 2 min 30 |
| 5 · **La voix** | **3 min** |
| 6 · Les garde-fous | 2 min |
| | **≈ 15 min** |

Si tu dois raccourcir, coupe l'acte 4 : l'acte 3 a déjà prouvé que le produit répond aux
questions techniques. **Ne coupe jamais le 3 ni le 6.**

---

## Plan B

| Problème | Je fais | Je dis |
| --- | --- | --- |
| Réponse lente | Attendre, ne pas recliquer | *Sta interrogando il modello e poi l'archivio.* |
| Mauvais résultat | **F5**, reprendre à l'acte 1 | *Mi porto dietro la conversazione precedente. Riparto pulito.* |
| Le schéma reste blanc | Bouton « ouvrir dans un onglet » | *Ve lo apro a parte.* |
| La voix ne démarre pas | Revenir au clavier, taper la question | *Continuo da tastiera.* |
| La voix dit « puntino » entre les étapes | Continuer, ne pas s'arrêter dessus | *(ne rien dire)* |
| Un service est tombé | `docker compose restart chat-service` puis F5 | *Un attimo, riavvio un servizio.* |
| L'extension déraille | `TECHNICAL_INFO_ENABLED=false` dans `.env`, `docker compose up -d chat-service` | *(30 secondes, retour au produit de base)* |

**Fais des captures de chaque acte la veille**, dans un onglet ouvert.

---

## Questions du client

**Da dove vengono queste informazioni?**
> Dal vostro archivio. Su questo solo veicolo: 122 fusibili con funzione e amperaggio, 21
> coppie di serraggio, 16 lampadine, 41 dati motore, 7 schemi elettrici e 36 capitoli di
> procedure. Nessuna era consultabile prima.

**Le informazioni le inventa?**
> No. L'intelligenza artificiale capisce la domanda e sceglie dove cercare. Il contenuto
> tecnico arriva dal database allo schermo senza passare dal modello.

**Quanto costa?**
> Frazioni di centesimo a domanda, misurato nella dashboard, non stimato. Indicizzare
> tutta la documentazione tecnica di questo veicolo è costato meno di due centesimi.

**Funziona con tutto il nostro archivio?**
> Quello che vedete è un veicolo solo, caricato in pochi minuti. Il procedimento è
> automatico.

**Quante lingue?**
> Cinque: italiano, francese, inglese, spagnolo e portoghese. Riconosce la lingua da come
> scrivete, senza nessun pulsante.

---

## Ce que tu ne dois PAS promettre

- **Les schémas n'existent qu'en italien et en anglais.** Fusibles, couples, ampoules et
  procédures sont dans les cinq langues, pas les 7 schémas.
- **55 images n'ont pas été livrées** — photos des boîtiers, emplacements de composants.
  On dit « F04, 50 A, compartiment moteur », on ne le montre pas.
- **Un seul véhicule est chargé.** C'est un choix, pas une limite technique — mais ne
  laisse pas croire que le catalogue complet est en base.
- **Le repli moteur partagé ne marche pas sur ce jeu de données.** Il faut deux véhicules
  partageant un moteur. Ne le mentionne pas.
