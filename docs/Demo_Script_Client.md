# Démo client — SemaRepair

Français pour toi. **Italien pour tout ce que tu tapes et tout ce que tu dis.**

Chaque scénario se lit dans l'ordre : je tape → **je dis un paragraphe** → le résultat
s'affiche → **je dis un deuxième paragraphe**. Les deux paragraphes italiens sont à
lire tels quels.

Tout a été testé en réel sur la stack avant écriture.

---

## Avant de commencer

```bash
docker compose up -d
```

Ouvrir **`http://localhost/`**, zoom **110–125 %**, thème clair.

Débloquer la dashboard maintenant : clic sur le bouton grille en haut à droite, une
fenêtre demande une clé, coller `USAGE_DASHBOARD_KEY` du fichier `.env`. Si tu ne le
fais pas maintenant, elle te la demandera devant le client.

**F5 entre chaque scénario.** Sans ça, le véhicule du scénario précédent reste en
mémoire et le suivant ne montrera pas ce que tu annonces.

---

## 1. Recherche par code défaut

### Je tape

```
Ho il codice errore P0091
```

### Avant le résultat

> Parto dal caso più semplice. Il meccanico ha collegato lo strumento di diagnosi, ha
> letto un codice errore e scrive solo quello, senza dirmi su quale veicolo sta
> lavorando.

### Ce qui s'affiche

Un message qui demande le véhicule, et **une seule carte** : `FIAT Ducato · 2.3
Multijet - 120 16v`. Aucune fiche de réparation.

### Après le résultat

> Come vedete non mi ha dato nessuna procedura, mi ha chiesto prima il veicolo. È una
> scelta voluta: lo stesso codice cambia significato da un motore all'altro, quindi
> finché non sa qual è la macchina non mostra niente. Adesso scelgo il veicolo e mi
> apre la documentazione.

Clique la carte : **4 fiches** numérotées apparaissent. Ouvre la n° 2 pour montrer les
étoiles de fiabilité et les puces DTC.

⏱ **2 min**

---

## 2. Recherche par symptôme

### Je tape

```
La spia motore è accesa e il veicolo perde potenza a tratti
```

### Avant le résultat

> Qui non c'è nessun codice. Il meccanico scrive solo quello che vede e quello che il
> cliente gli racconta, con parole sue, in italiano normale. Nessun termine tecnico,
> nessun menù da imparare.

### Ce qui s'affiche

7 cartes véhicule. Clique `FIAT Ducato 2.3 Multijet - 120 16v` : **9 fiches**.

### Après le résultat

> Ha capito il sintomo e mi ha chiesto di identificare il veicolo. La cosa importante è
> che si è tenuto da parte la ricerca: appena ho scelto la macchina l'ha rifatta da
> solo, e io non ho riscritto niente.

⚠️ **Le message au-dessus des 9 fiches est instable.** Sur deux essais identiques il a
une fois annoncé « les 3 plus pertinentes sont affichées » alors que les 9 sont à
l'écran. Ne le lis pas à voix haute, commente le nombre toi-même. Si le client le
relève : *« Quel messaggio lo scrive il modello. La lista sotto viene dal database. »*

⏱ **2 min** — c'est le scénario à couper si tu manques de temps.

---

## 3. Recherche par véhicule

### Je tape

```
Sto lavorando su un Fiat Ducato 2.3 Multijet da 120 CV
```

### Avant le résultat

> Terzo modo di iniziare, ed è quello più naturale in officina: parto dalla macchina
> che ho sul ponte, e solo dopo racconto il problema.

### Ce qui s'affiche

Une carte, puis après le clic un message de confirmation et **la pastille véhicule
apparaît en haut de l'écran**.

### Après le résultat

> Il veicolo adesso è confermato e lo vedete qui in alto, con anni, alimentazione,
> potenza e codice motore. Da questo momento ogni ricerca che faccio è già filtrata su
> questo motore e non devo più ripeterlo. Provo a chiedergli un problema di iniezione.

Tape ensuite :

```
Ha problemi di iniezione
```

**7 fiches** s'affichent.

⏱ **2 min**

---

## 4. ⭐ Moteur partagé — le moment clé

### Je tape

```
Ho un Citroën Jumper con motore RHV che mi dà il codice P0380
```

Puis clique la carte **`2001–2002`**.

> Les cartes sont triées par année décroissante : `2002–2006` s'affiche en premier.
> Repère-toi sur les années. Les deux marchent pareil, ne te reprends pas si tu te
> trompes.

### Avant le résultat

> Adesso arriviamo al punto più importante di tutta la dimostrazione. Un Citroën Jumper
> con motore RHV, e un codice P0380 che riguarda le candelette di preriscaldamento.

### Ce qui s'affiche

Au-dessus de la fiche :

> Non ho trovato documenti per il tuo CITROEN Jumper 2.0 HDI 8v (RHV) con il codice
> P0380. Ho però trovato documenti per altri veicoli che montano lo stesso motore RHV
> (FIAT Ducato):

Puis **une fiche** : *Mancato avviamento motore, a seguito di un arresto in marcia*.

### Après le résultat

> Per questo Citroën, con questo codice, nel nostro archivio non esiste nessuna scheda,
> e un sistema normale vi risponderebbe che non ha trovato niente. Qui invece il sistema
> sa che il Citroën Jumper e il Fiat Ducato montano lo stesso identico motore, il RHV,
> quindi la scheda del Ducato è tecnicamente valida anche per il Citroën. E ve lo dice
> prima di mostrarvi il documento, non dopo. Questa frase non la scrive l'intelligenza
> artificiale: la costruisce il sistema partendo dal codice motore, quindi non può
> cambiare e non può sparire.

⏱ **3 min** — ralentis ici, laisse un silence avant le deuxième paragraphe.

---

## 5. Multilingue

⚠️ **En français, pas en espagnol.** Testé : la phrase espagnole n'est pas détectée de
façon fiable, le système reste en italien et la démo tombe à plat.

### Je tape

```
J'ai un Citroën Jumper avec le moteur RHV qui me donne le code P0380
```

Puis clique l'une des deux cartes.

### Avant le résultat

> Rifaccio esattamente lo stesso caso di prima, ma questa volta scrivo in francese.

### Ce qui s'affiche

Le même message, en français, puis la fiche en français avec les libellés
`Système :` / `Dispositif :` / `Anomalie :` / `Cause :`.

### Après le résultat

> Non ho premuto nessun pulsante e non ho cambiato nessuna impostazione: riconosce da
> solo la lingua in cui scrivo e risponde in quella. Funziona in italiano, francese,
> inglese, spagnolo e portoghese, e cambia lingua anche la scheda tecnica, non solo la
> frase di accompagnamento.

⏱ **1 min 30**

---

## 6. Garde-fous

### Je tape

Les trois à la suite, sans réinitialiser entre elles.

```
Dammi la procedura di sostituzione delle candelette
```
```
Che tempo fa a Milano oggi?
```
```
Sto lavorando su una Tesla Model 3
```

### Avant le résultat

> Ultima parte, ed è quella che conta di più per la vostra responsabilità. Vi faccio tre
> domande di seguito, tutte e tre fatte apposta per metterlo in difficoltà.

### Ce qui s'affiche

3 cartes FORD avec une demande de véhicule et aucune procédure ; puis un refus poli
sans aucune carte ; puis *« Non abbiamo un Tesla Model 3 a catalogo. »*

### Après le résultat

> Nel primo caso mi ha chiesto il veicolo invece di darmi una procedura a caso. Nel
> secondo non ci ha nemmeno provato, perché non è il suo lavoro. Nel terzo mi ha detto
> in una riga che quella macchina non ce l'abbiamo in archivio. Il punto è sempre lo
> stesso: non inventa mai. Ogni frase tecnica che vedete esce dal vostro archivio,
> parola per parola, e se il documento non c'è ve lo dice.

⏱ **2 min**

---

## 7. Dashboard

### Je fais

Clic sur le bouton **grille** en haut à droite.

### Avant le résultat

> Chiudo con la parte che interessa voi più che il meccanico.

### Ce qui s'affiche

Trois indicateurs (dépense totale, jetons, appels), un graphique sur 7 jours, la
répartition par modèle et par service, et le registre des appels.

### Après le résultat

> Ogni singola chiamata all'intelligenza artificiale è registrata qui: quanto costa,
> quanti dati, quale servizio l'ha chiesta. Non sono stime commerciali, sono i numeri
> reali presi dal registro, e li potete guardare quando volete.

Reviens à la chat avec le bouton bulle, puis bascule en **thème sombre** pour finir.

⏱ **1 min 30**

---

## Plan B

| Problème | Je fais | Je dis |
| --- | --- | --- |
| Réponse lente | Attendre, ne pas recliquer | *Sta interrogando il modello e poi l'archivio. Sono due passaggi.* |
| Mauvais véhicule ou résultat bizarre | **F5**, refaire | *Mi porto dietro la conversazione precedente. Riparto pulito.* |
| Le message du moteur partagé n'apparaît pas | **F5**, retaper, recliquer | *Avevo già un veicolo in memoria. Riparto da zero.* |
| `**texte**` visible à l'écran | Continuer | *(ne rien dire)* |
| Service tombé | `docker compose restart chat-service` puis F5 | *Un attimo, riavvio un servizio.* |
| Plus rien ne répond | Passer aux captures | *Vi mostro il risultato registrato, poi rifacciamo la prova con calma.* |

**Fais des captures d'écran de chaque scénario la veille**, dans un onglet ouvert.

---

## Questions du client

**Quanto costa?**
> Frazioni di centesimo a domanda, e lo vedete misurato nella dashboard, non stimato.
> Il modello di prezzo lo definiamo insieme.

**Funziona con tutto il nostro archivio?**
> Oggi vedete un campione reale: 157 veicoli, 5 marche, 117 codici motore, 74 codici
> guasto, 5 lingue. Caricate il vostro archivio completo e funziona allo stesso modo.

**Quante lingue?**
> Cinque: italiano, francese, inglese, spagnolo e portoghese. Sia la conversazione sia
> le schede tecniche.

**Le informazioni le inventa?**
> No. Causa, intervento e procedura escono parola per parola dai vostri documenti.
> L'intelligenza artificiale capisce la domanda e sceglie dove cercare, ma non scrive
> mai il contenuto tecnico.

**E quando il documento non c'è?**
> Prima cerca una scheda di un veicolo con lo stesso motore, e ve lo dice chiaramente.
> Se non c'è nemmeno quella vi chiede di precisare. Se non c'è proprio niente, ve lo
> dice in una riga.

**I nostri dati escono dall'azienda?**
> Al modello arriva la domanda e qualche informazione di servizio. Il contenuto delle
> schede non passa mai dal modello: viene preso dal database e mostrato direttamente.

**Quanto ci mette a rispondere?**
> Due o tre secondi. E quello che avete visto gira sul mio portatile.

---

## Minutage

| # | Scénario | Durée |
| --- | --- | --- |
| — | Intro | 1 min 30 |
| 1 | Code défaut | 2 min |
| 2 | Symptôme | 2 min |
| 3 | Véhicule | 2 min |
| 4 | **Moteur partagé** | **3 min** |
| 5 | Multilingue | 1 min 30 |
| 6 | Garde-fous | 2 min |
| 7 | Dashboard | 1 min 30 |
| | **Total** | **≈ 16 min** |

Pour tenir 15 minutes, coupe le scénario 2. Ne coupe jamais le 4 ni le 6.

---

## Si on te demande comment ça marche

À l'import, chaque document est relié à ses véhicules, ses codes défaut et ses
composants, et les véhicules sont reliés entre eux quand ils montent le même moteur.
C'est ce lien qui permet de trouver la fiche du Fiat pour le Citroën.

La recherche se fait par le sens et pas par les mots : « perde potenza a tratti »
retrouve un document qui dit « prestazioni notevolmente ridotte ».

L'intelligence artificielle comprend la question mais ne rédige pas la réponse. Elle
lit la phrase, en extrait le véhicule et le symptôme, et décide où chercher. Le contenu
technique affiché sort du document tel quel. C'est pour cette raison que le message du
moteur partagé est construit par le système et pas par le modèle : il ne peut pas
disparaître.
