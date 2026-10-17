# CatalogueMaison

Gestion du catalogue des médias possédés à la maison : livres, musique,
films, jeux. Architecture microservices, multitenant par ménage.

L'architecture, les conventions et les décisions prises sont décrites dans
[CLAUDE.md](CLAUDE.md) — à lire avant toute contribution.

## Démarrage

```bash
# Infrastructure locale (PostgreSQL + Keycloak)
tools/dev.sh up

# Bases des services
tools/dev.sh migrate-all        # un schéma par ménage, dans la base catalogue
tools/dev.sh metadata-migrate   # cache d'enrichissement, base metadata

# Services, chacun dans son terminal
tools/dev.sh api                # Catalogue.Api  — http://localhost:5080
tools/dev.sh metadata           # Metadata.Api   — http://localhost:5081
tools/dev.sh front-start        # frontend       — http://localhost:4200
```

- PostgreSQL sur `localhost:5432` — utilisateur `maison`, mot de passe `dev`
- Keycloak sur `http://localhost:8080` — console `admin` / `admin`
- Realm `maison`, importé depuis `keycloak/realm-maison.json`

Les comptes de développement sont créés par l'import du realm. Voir
`keycloak/README.md`.

## Scan d'un code-barres

L'écran « Ajouter » accepte un ISBN ou un code-barres, tapé, envoyé par une
douchette USB (qui se comporte comme un clavier), ou lu par la caméra. La
fiche est pré-remplie, puis validée ou corrigée avant d'être enregistrée.

- **Livres et BD** : catalogue de la BnF, puis Open Library en repli.
- **Disques** : MusicBrainz.
- **Films** : pas de code-barres (OMDb ne les connaît pas), mais une
  **recherche par titre** via OMDb, si une clé est configurée (ci-dessous).
- **Jeux** : pas de source ; saisie manuelle.

La caméra n'est accessible au navigateur qu'en contexte sécurisé :
`localhost`, ou HTTPS.

## Recherche de films (OMDb)

La recherche de films par titre n'est proposée que si Metadata.Api dispose
d'une clé OMDb (gratuite, 1 000 requêtes par jour : <https://www.omdbapi.com/apikey.aspx>).
Sans clé, l'écran « Ajouter » ne l'affiche pas, et tout le reste fonctionne.

La clé est un secret : jamais dans le dépôt ni dans `appsettings.json`. En
développement, elle va dans les user-secrets de Metadata.Api :

```bash
dotnet user-secrets set "Enrichment:OmdbApiKey" "<votre-clé>" --project services/Metadata.Api
```

Ailleurs, par l'environnement : `Enrichment__OmdbApiKey`. Relancer
`tools/dev.sh metadata` pour la prendre en compte. OMDb décrit les films en
anglais (« Inception », « Action, Sci-Fi ») : le titre se corrige dans la
fiche avant d'enregistrer, si l'on préfère le titre français.

## Essayer depuis un téléphone

```bash
tools/dev.sh front-lan   # frontend en HTTPS sur le réseau local, port 4443
tools/dev.sh api         # à (re)lancer après : il accepte alors l'issuer du relais
```

Ouvrir sur le téléphone l'adresse affichée (`https://<ip-du-mac>:4443`), et
accepter une fois le certificat auto-signé.

Le serveur de développement sert alors de **relais** : la page, l'API (sous
`/api`) et Keycloak (sous `/realms`) partagent la même origine HTTPS. C'est ce
qui rend la caméra disponible, évite le contenu mixte et dispense de CORS ; le
téléphone n'a de toute façon pas accès au `localhost` du Mac.

Keycloak tire l'issuer de ses jetons de l'URL par laquelle on le joint : à
travers le relais, c'est l'adresse vue par le téléphone. `tools/dev.sh api`
l'ajoute aux issuers acceptés par Catalogue.Api, par l'environnement
(`Keycloak__AdditionalIssuers__0`). Si l'adresse du Mac change, relancer
`front-lan` puis `api`.
