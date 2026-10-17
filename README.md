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
- **Films et jeux** : pas de source gratuite et sans clé ; saisie manuelle.

La caméra n'est accessible au navigateur qu'en contexte sécurisé :
`localhost`, ou HTTPS. Depuis un téléphone sur le réseau local en HTTP, seul
le champ de saisie fonctionne.
