# Configuration Keycloak

Ce dossier est la **seule source** de la configuration du realm. Toute
modification faite à la main dans la console d'administration doit être
reportée ici, sinon elle disparaît à la prochaine recréation de
l'environnement.

Le realm est importé au démarrage du conteneur
(`start-dev --import-realm`). Pour réimporter après modification :

```bash
docker compose -f docker/docker-compose.yml restart keycloak
```

L'import ne recharge un realm déjà présent que si le conteneur est recréé.
Pour repartir d'un realm propre :

```bash
docker compose -f docker/docker-compose.yml down keycloak
docker compose -f docker/docker-compose.yml up -d keycloak
```

## Ce que contient le realm

Un **realm unique** (`maison`) partagé par tous les ménages — pas de realm
par ménage.

### Le claim `tenant`

Le ménage vit dans l'attribut utilisateur `tenant`, exposé dans le jeton par
le protocol mapper du client scope `tenant`, inclus par défaut dans tous les
clients. C'est le **seul** endroit où un service lit le ménage.

### Clients

| Client | Type | Rôle |
|---|---|---|
| `catalogue-frontend` | public | Angular, Authorization Code + PKCE S256 |
| `catalogue-api` | confidentiel | valide les bearer ; appelle `metadata-api` |
| `metadata-api` | confidentiel | appelé en service à service |
| `tenants-api` | confidentiel | **seul** client avec accès à l'Admin REST API |

Les secrets de ce fichier (`dev-*`) sont des secrets de développement. Ils
ne doivent jamais servir ailleurs qu'en local.

### Comptes de développement

Tous avec le mot de passe `dev`.

| Compte | Ménage | Rôles | Sert à |
|---|---|---|---|
| `alice` | `martin` | adulte, admin-ménage | usage courant |
| `theo` | `martin` | enfant | vérifier les règles par rôle |
| `bruno` | `durand` | adulte, admin-ménage | **test d'isolation** : ne doit rien voir du ménage `martin` |
| `orphelin` | *aucun* | adulte | vérifier qu'un jeton sans claim `tenant` est bien rejeté en 401 |

`orphelin` et `bruno` ne sont pas des comptes de confort : ils sont les
témoins des deux garde-fous du modèle multitenant. Ne pas les supprimer.
