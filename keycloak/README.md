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

## Pas de « mot de passe oublié »

Aucun serveur de courriel n'est configuré, ni prévu : la réinitialisation
du mot de passe par courriel est donc désactivée (`resetPasswordAllowed:
false`), et le lien n'apparaît pas sur la page de connexion. Un mot de passe
perdu se réinitialise par l'administration du ménage. Ne pas réactiver
l'option sans configurer d'abord `smtpServer` : la page s'afficherait, mais
aucun courriel ne partirait.

## Thème de connexion

`themes/maison` donne aux pages de Keycloak (connexion, erreur…) le style du
site, et le realm les affiche en français
(`defaultLocale: fr`). Il est monté dans le conteneur par
`docker-compose.yml`, et désigné par `loginTheme` dans le realm.

Le thème **hérite** de `keycloak.v2` et ne remplace que la feuille de style
(`login/resources/css/maison.css`) : aucun gabarit n'est copié, si bien que
tous les écrans et le comportement de Keycloak restent ceux du parent, et que
le thème survit aux montées de version. Ce n'est pas une extension Java — ni
SPI, ni JAR —, seulement des ressources statiques.

Les couleurs et les polices sont recopiées de `frontend/src/styles.scss`,
faute de feuille commune : les modifier aux deux endroits. En `start-dev`,
Keycloak ne met pas les thèmes en cache — une modification de la feuille se
voit au rechargement de la page, sans redémarrage.

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
| `metadata-api` | confidentiel | appelé en service à service, n'appelle personne |
| `tenants-api` | confidentiel | **seul** client avec accès à l'Admin REST API |

### Accès depuis le réseau local

Le client `catalogue-frontend` accepte, en plus de `http://localhost:4200`,
l'URI de redirection **relative** `/*`. Keycloak la résout contre l'URL par
laquelle on le joint : à travers le relais HTTPS du frontend
(`tools/dev.sh front-lan`), c'est l'origine vue par le téléphone. Aucune
adresse IP n'est ainsi écrite dans ce fichier. Jointe directement, la même
URI se résout vers Keycloak lui-même, ce qui n'ouvre rien.

Keycloak lit pour cela les en-têtes `X-Forwarded-*`
(`KC_PROXY_HEADERS=xforwarded` dans `docker-compose.yml`). C'est un réglage
de développement : en production, ces en-têtes ne doivent être acceptés que
d'un reverse proxy de confiance.

### Service à service : `catalogue-api` → `metadata-api`

Catalogue.Api obtient **son propre** jeton en client credentials ; le jeton
de l'utilisateur n'est jamais relayé à Metadata.Api. Ce jeton porte :

- l'audience `metadata-api`, via le scope `metadata-api-audience` ;
- le rôle de realm `enrichissement`, attribué au seul compte
  `service-account-catalogue-api`, et exigé par Metadata.Api ;
- **aucun** claim `tenant` : l'enrichissement est commun à tous les
  ménages, et ne doit rien savoir de celui qui scanne.

Un jeton d'utilisateur ne porte ni cette audience ni ce rôle : le frontend
ne peut pas appeler Metadata.Api directement, il passe par Catalogue.Api.

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
