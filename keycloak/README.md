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

L'import ne recharge **pas** un realm déjà présent : un simple `restart` ne
suffit donc pas après avoir modifié ce fichier. Il faut recréer le conteneur,
ce que fait :

```bash
tools/dev.sh keycloak-reimport
```

Keycloak tourne en `start-dev`, donc sur une base en mémoire : recréer le
conteneur repart d'un realm propre, et les comptes de développement sont
recréés par l'import.

## Piège : `clientScopes` remplace les scopes intégrés

À l'import, déclarer un tableau `clientScopes` **remplace** l'ensemble des
client scopes intégrés de Keycloak au lieu de s'y ajouter. Un export qui ne
listerait que les scopes maison partirait donc sans `profile`, `email`,
`roles`, `web-origins`, `acr` ni `basic`.

Les symptômes ne désignent pas la cause :

- Keycloak refuse l'autorisation avec `Invalid scopes: openid profile email`,
  et redirige vers l'application avec une erreur — côté frontend, on ne voit
  qu'un échec de l'échange du code.
- Plus insidieux encore si le frontend ne demandait pas ces scopes : le jeton
  serait émis **sans `realm_access`**, donc sans aucun rôle, et l'API
  rejetterait en 403 des comptes pourtant correctement configurés.

C'est pourquoi ce fichier contient les six scopes intégrés en entier. Leurs
définitions ont été relevées sur le realm `master` d'une instance neuve, et
non écrites à la main. **Ne pas les retirer** en les croyant redondants.

## Les rôles doivent être dans les deux jetons

Le mapper « realm roles » du scope `roles` écrit `realm_access` dans le jeton
d'accès **et** dans le jeton d'identité (`id.token.claim`), ce qui s'écarte
de la définition intégrée de Keycloak. C'est voulu, et les deux
destinataires sont différents :

- l'**API** valide le jeton d'accès et y lit les rôles pour ses policies ;
- le **frontend** ne lit que le jeton d'identité — c'est le seul que la
  librairie OIDC expose. Sans cette ligne, l'interface ne voit aucun rôle et
  masque ses actions d'écriture à des comptes qui y ont pourtant droit.

Faire décoder le jeton d'accès par le frontend serait l'autre solution, et
elle est écartée : ce jeton est destiné à l'API, pas au client.

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
