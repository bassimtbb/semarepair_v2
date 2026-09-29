# Déploiement SemaRepair

Cible : **`forj-dkr-dev-01`**, Ubuntu 24.04.4 LTS, `172.16.40.151`, avec IIS
(`FORJ-IIS-C1-D1`) en frontal.

> Ce document remplace une première version écrite pour WSL2 sur le serveur IIS. Cette
> piste est abandonnée : le serveur IIS est une VM sans virtualisation imbriquée, WSL2 ne
> peut pas y démarrer. La machine Linux dédiée fournie par Andrea est une meilleure
> solution de toute façon — aucun redémarrage du serveur IIS n'est nécessaire.

---

## 0. Ce qui est déjà vérifié sur la machine Linux

Rien à installer. Vérifié en session le 28/09 :

| | |
| --- | --- |
| Docker | **29.6.2**, Compose **v5.3.1** |
| Droits | utilisateur dans le groupe `docker` (pas besoin de `sudo`) |
| Disque | 44 Go libres sur 58 |
| Mémoire / CPU | 7,8 Go / **2 vCPU** |
| Accès sortant | `generativelanguage`, `texttospeech`, `download.docker.com`, `registry-1.docker.io` tous joignables |

**2 vCPU seulement** : le premier build compile trois services .NET et l'application
Angular. Compte 10 à 20 minutes, c'est normal.

On y accède depuis le serveur IIS : `ssh btabbeb@172.16.40.151`. Le VPN seul ne suffit
pas — le sous-réseau `.40` n'est pas routé depuis les clients VPN en `.41`.

---

## 1. Récupérer le code

```bash
cd ~
git clone https://github.com/bassimtbb/semarepair_v2.git
cd semarepair_v2 && git checkout dev
```

Dépôt privé : GitHub demandera un **token personnel**, pas le mot de passe.

`Data/` est versionné (documents, schémas, dump) ; seul `.env` ne l'est pas.

## 2. Créer le `.env`

```bash
cat > .env <<'EOF'
POSTGRES_DB=semarepair
POSTGRES_USER=semarepair
POSTGRES_PASSWORD=<mot de passe fort>
GEMINI_API_KEY=<clé>
GOOGLE_CLOUD_TTS_API_KEY=<clé>
USAGE_DASHBOARD_KEY=<secret>
TECHNICAL_INFO_ENABLED=true
EOF
```

Sans `TECHNICAL_INFO_ENABLED=true`, toute l'extension technique est éteinte : plus de
fusibles, plus de schémas, plus de procédures.

## 3. Vérifier que le port 80 est libre

```bash
ss -tlnp | grep ':80 ' || echo "port 80 libre"
```

S'il l'est, **ne touche pas au `docker-compose.yml`** : IIS relaiera vers
`http://172.16.40.151/`. La bascule sur 8080 n'était nécessaire que dans l'hypothèse
abandonnée où tout tournait sur le serveur IIS.

> ### ⚠️ Neutraliser `docker-compose.override.yml`
>
> Compose charge ce fichier **automatiquement**, sans qu'on le nomme. Il est écrit pour
> le développement et publie quatre ports sur `0.0.0.0` : PostgreSQL 5432, search 5001,
> vehicle 5002, chat 5000.
>
> Deux conséquences sur une machine partagée, rencontrées toutes les deux le 29/09 : la
> base devient joignable depuis tout le réseau avec le mot de passe du `.env`, et le 5000
> entre en collision avec un autre projet déjà hébergé sur la machine (`jtruck-api`), ce
> qui empêche `chat-service` de démarrer — donc aussi nginx, qui en dépend.
>
> Une ligne dans le `.env` du serveur règle les deux, définitivement :
>
> ```bash
> echo 'COMPOSE_FILE=docker-compose.yml' >> .env
> ```
>
> Compose ne charge plus alors que le fichier de base, pour **toutes** les commandes
> (`up`, `ps`, `logs`, `down`). `.env` n'étant pas versionné, le poste de développement
> garde ses ports. Contrôle : `docker compose ps` ne doit montrer qu'une seule
> publication, `0.0.0.0:80->80/tcp` sur nginx.

## 4. Charger les données

Deux voies. **La restauration est préférable** : plus rapide, et gratuite.

### a) Restaurer un dump

```bash
docker compose up -d our-postgres
docker compose ps                       # attendre "healthy"

docker compose cp Data/<dump>.sql.gz our-postgres:/tmp/dump.sql.gz
docker compose exec -T our-postgres sh -c "gunzip -c /tmp/dump.sql.gz | psql -U semarepair -d semarepair"
docker compose up -d --build            # l'ingestion dira "nothing to do"
```

L'extension `vector` est **incluse dans le dump** — le piège du §4b ne se pose pas ici.

### b) Réingérer depuis les sources

> ### ⚠️ Créer l'extension `vector` AVANT l'ingestion
>
> Sur une base neuve, `ingestion-resx` échoue avec :
> ```
> psycopg2.ProgrammingError: vector type not found in the database
> ```
> `register_vector()` s'exécute avant que le schéma n'ait créé l'extension. C'est un
> défaut d'ordonnancement connu, reproduit deux fois. Le contournement :
>
> ```bash
> docker compose up -d our-postgres
> docker compose exec -T our-postgres psql -U semarepair -d semarepair \
>   -c "CREATE EXTENSION IF NOT EXISTS vector;"
> ```
>
> Puis seulement :
> ```bash
> docker compose up ingestion
> docker compose up ingestion-resx      # long : embeddings Gemini, payants
> docker compose up -d --build
> ```

## 5. Vérifier avant de toucher à IIS

```bash
docker compose exec -T our-postgres psql -U semarepair -d semarepair \
  -c "SELECT count(*) FROM knowledge_chunks;"

curl -s -o /dev/null -w "%{http_code}\n" http://localhost/
curl -s "http://localhost/api/search/technical?q=fusibile&codiceMotore=169%20A%204000&marca=FIAT&lang=it" | head -c 200
```

Si ça échoue ici, le problème est dans la stack. Inutile d'aller plus loin.

---

## 6. IIS en reverse proxy

### 6.1 Installer

Télécharger les MSI directement — Web Platform Installer est retiré depuis 2022 :

1. **URL Rewrite 2.1** — `iis.net/downloads/microsoft/url-rewrite`
2. **ARR 3.0** — `iis.net/downloads/microsoft/application-request-routing`, **après** URL Rewrite

### 6.2 Activer le proxy

Nœud serveur → **Application Request Routing Cache** → **Server Proxy Settings** :

- ☑ **Enable proxy**
- **Response buffer threshold = 0**

> ### ⚠️ Le tampon qui casse le chat
>
> `POST /api/chat/stream` renvoie du `text/event-stream`, par morceaux. ARR met la
> réponse entière en tampon par défaut : le chat paraît figé, puis tout tombe d'un bloc.
> Le `nginx.conf` du projet a déjà `proxy_buffering off` pour la même raison ; ARR est une
> seconde couche qui doit faire pareil.

### 6.3 Le site

Chemin physique : un dossier **vide** — IIS ne sert aucun fichier, il relaie.
Binding : **https**, 443, le nom d'hôte, avec le certificat.

> ### ⚠️ Le HTTPS n'est pas optionnel
>
> Le mode vocal utilise `getUserMedia`. Les navigateurs ne l'autorisent que sur une
> origine sécurisée. En `http://` vers une machine distante, le micro est refusé sans
> message clair. **Sans certificat, pas de démo vocale.**

### 6.4 `web.config`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <!-- Tout est relaye. Le nginx du projet fait deja le routage interne
             (/api/chat, /api/search, /api/vehicles, /assets/pdf, le SPA) :
             le dupliquer ici serait deux endroits a tenir a jour. -->
        <rule name="SemaRepair" stopProcessing="true">
          <match url="(.*)" />
          <action type="Rewrite" url="http://172.16.40.151/{R:1}" />
        </rule>
      </rules>
    </rewrite>
    <urlCompression doStaticCompression="false" doDynamicCompression="false" />
  </system.webServer>
</configuration>
```

---

## 7. Vérification finale

| # | Test | Attendu |
| --- | --- | --- |
| 1 | `docker compose ps` | tout `Up`, postgres `healthy` |
| 2 | `curl http://localhost/` depuis la VM Linux | du HTML |
| 3 | `https://<hôte>/` depuis ton poste | l'interface |
| 4 | Le cadenas Chrome | **valide** — sinon pas de micro |
| 5 | Identifier le véhicule, cliquer la carte | la pastille apparaît |
| 6 | Décrire une panne | des fiches, **arrivant progressivement** |
| 7 | Demander un fusible | la valeur et sa position |
| 8 | Demander un schéma | le dessin dans le cadre |
| 9 | Bouton **Voice HD** | la voix répond |

Le **6** révèle le tampon ARR : si la réponse arrive d'un bloc après une longue attente,
retour au §6.2. Le **8** échoue si `Data/PDF/` n'a pas été transféré.

## 8. Garde le repli

**Ne démonte pas la stack de ton poste** tant que les neuf tests ne sont pas verts.

Deux dumps sont versionnés : `Data/demo_fi2524.sql.gz` (Fiat 500) est le jeu courant,
celui qui est déployé et vérifié ; `Data/demo_fi0396.sql.gz` (Ducato) est le jeu
précédent, gardé parce qu'il fonctionne toujours.
