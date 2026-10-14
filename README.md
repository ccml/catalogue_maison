# CatalogueMaison

Gestion du catalogue des médias possédés à la maison : livres, musique,
films, jeux. Architecture microservices, multitenant par ménage.

L'architecture, les conventions et les décisions prises sont décrites dans
[CLAUDE.md](CLAUDE.md) — à lire avant toute contribution.

## Démarrage

```bash
# Infrastructure locale (MySQL + Keycloak)
docker compose -f docker/docker-compose.yml up -d
```

- MySQL sur `localhost:3306` — utilisateur `maison`, mot de passe `dev`
- Keycloak sur `http://localhost:8080` — console `admin` / `admin`
- Realm `maison`, importé depuis `keycloak/realm-maison.json`

Les comptes de développement sont créés par l'import du realm. Voir
`keycloak/README.md`.
