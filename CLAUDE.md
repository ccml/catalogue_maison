# CatalogueMaison

Application web de gestion du catalogue des médias possédés à la maison :
livres, musique, films, jeux, etc.

## Architecture

Architecture microservices : un frontend Angular et plusieurs Web API .NET,
chacune dans son propre sous-dossier à la racine du dépôt.

```
/frontend              Application Angular
/services
  /Catalogue.Api       Médias possédés, collections, recherche, prêts
  /Metadata.Api        Enrichissement (ISBN / code-barres, APIs externes)
  /Tenants.Api         Registre des ménages, profils applicatifs
/gateway               (optionnel) reverse proxy / API Gateway
/docker                docker-compose, Dockerfiles, init PostgreSQL + Keycloak
/keycloak              Export du realm, thèmes, mappers (versionnés)
/tools                 Administration : provisioning des ménages, migrations
```

### Responsabilités

- **Catalogue.Api** — source de vérité du catalogue. Un média y est créé avec
  le minimum requis (type, titre) ; les métadonnées enrichies y sont stockées
  dénormalisées, telles que renvoyées par Metadata.Api.
- **Metadata.Api** — résout un code-barres / ISBN en métadonnées via les APIs
  externes, met en cache les résultats. N'écrit jamais dans le catalogue :
  Catalogue.Api l'appelle et décide quoi conserver.
- **Tenants.Api** — registre des ménages et données applicatives de
  l'utilisateur (préférences, rôle dans le ménage). **N'authentifie pas et
  n'émet aucun jeton** : c'est Keycloak qui le fait. Aucun mot de passe,
  aucune identifiant de connexion ne doit être stocké ici.
- **Keycloak** — fournisseur d'identité (hors dépôt, conteneurisé). Seule
  source de l'authentification et de l'émission des jetons.

Chaque service possède sa propre base PostgreSQL et ne touche jamais à
celle d'un autre. Aucune jointure SQL entre services. Toute communication inter-services passe par HTTP sur les
contrats publics.

## Stack

- **Frontend** : Angular (standalone components, signals), TypeScript strict
- **Backend** : .NET / ASP.NET Core Minimal APIs, C#
- **Base de données** : PostgreSQL, accès via EF Core 10 (provider
  `Npgsql.EntityFrameworkCore.PostgreSQL`). Migrations EF versionnées dans
  le dépôt, une par service.
- **Authentification** : OAuth2 / OpenID Connect via **Keycloak**
- **Conteneurisation** : Docker Compose pour le dev local (PostgreSQL +
  Keycloak)

### Pourquoi PostgreSQL et non MySQL

Décision prise à la mise en place, en connaissance de cause. Le projet cible
.NET 10 / EF Core 10 ; or `Pomelo.EntityFrameworkCore.MySql` plafonne à
EF Core 9 (dernière version 9.0.0, août 2025) et son mainteneur n'a plus
d'activité publique depuis décembre 2025. Les options restantes côté MySQL
étaient un fork communautaire en preview ou le provider Oracle, dont les
limitations de mapping JSON touchent précisément le cœur du modèle hybride
ci-dessous.

Npgsql suit les versions d'EF Core, et `jsonb` est un meilleur support du
modèle hybride que le JSON de MySQL. Ne pas revenir à MySQL sans retrancher
cette contrainte.

## Authentification

OAuth2 / OpenID Connect, **Keycloak comme unique fournisseur d'identité**.
Aucun service .NET n'émet, ne renouvelle ni ne valide de mot de passe.

### Frontend Angular

- **Authorization Code Flow + PKCE**, client **public**. Jamais d'Implicit
  Flow, jamais de client secret embarqué dans le frontend.
- Jeton d'accès conservé en mémoire ; le rafraîchissement passe par la
  librairie OIDC. Ne pas écrire de jeton dans `localStorage`.
- L'interface ne tire **aucune règle de sécurité** des claims : elle les
  utilise pour l'affichage, l'autorisation réelle est systématiquement
  revalidée côté API.

### Backend .NET

- Validation **JWT bearer** contre le JWKS du realm. Vérifier `issuer`,
  `audience` et la signature — ne jamais désactiver une de ces validations,
  même en développement.
- Les services ne communiquent pas avec Keycloak à chaque requête : le jeton
  est auto-porteur. Pas d'appel d'introspection sur le chemin chaud.
- Appels de service à service : **client credentials**, client confidentiel
  propre à chaque service. Ne pas relayer le jeton de l'utilisateur entre
  services sauf besoin explicite d'agir en son nom.
- Autorisation par **rôles de realm** mappés en policies ASP.NET Core. Le
  rôle dans le ménage (par ex. adulte / enfant) vit dans Keycloak, pas en
  base applicative.

### Développement local

Keycloak tourne dans Docker Compose avec le realm importé depuis
`/keycloak`. Les comptes de développement sont créés par l'import, jamais
en dur dans le code ni dans les tests.

## Multitenant

Un **tenant = un ménage** (une famille). Toute donnée du catalogue appartient
à un ménage et n'est jamais visible d'un autre.

Un utilisateur appartient à **un seul ménage** : le ménage est une propriété
de l'utilisateur, pas une sélection à l'exécution. Pas de notion de « ménage
actif ».

### Isolation : un schéma PostgreSQL par ménage

La base `catalogue` contient **un schéma par ménage** (`martin`, `durand`,
…) ; `tenants` et `metadata` sont des bases à part, pour les raisons
ci-dessous. L'isolation est portée par le schéma, il n'y a donc **pas de
colonne `TenantId`** dans les tables métier — le schéma *est* la frontière.

Le schéma `public` n'est jamais celui d'un ménage, et ne contient aucune
table métier : un `search_path` mal résolu doit échouer bruyamment plutôt
que retomber silencieusement sur des données.

- `Tenants.Api` utilise une **base dédiée, non partitionnée**. C'est lui qui tient le
  registre des ménages, nécessaire avant de savoir quel schéma adresser : il
  ne peut pas être lui-même partitionné par ménage. Le `TenantId` y est une
  colonne du profil utilisateur.
- `Metadata.Api` est **tenant-agnostique**, et c'est volontaire : les
  métadonnées d'un ISBN sont universelles. Le cache d'enrichissement est
  partagé par tous les ménages. Ne pas le partitionner.
- `Catalogue.Api` est le seul service réellement multi-schéma.

### Résolution du tenant

Le ménage est lu dans le **claim `tenant` du jeton Keycloak**, et nulle part
ailleurs. Jamais depuis l'URL, le corps de la requête, un paramètre de query
ou un en-tête fourni par le client — un client ne doit pas pouvoir désigner
le ménage sur lequel il travaille.

Le claim est produit par un **protocol mapper** Keycloak à partir d'un
attribut utilisateur, dans un **realm unique** partagé par tous les ménages.
Pas de realm par ménage. Un jeton sans claim `tenant` exploitable est
rejeté en 401 : jamais de ménage par défaut, jamais de repli.

**Seule exception : l'opérateur de la plateforme.** Un compte portant le
rôle de realm `admin-plateforme` et **aucun** claim `tenant` crée et
supprime les ménages, par les endpoints `/households` de `Tenants.Api`
(marqués `PlatformEndpoint`), et nulle part ailleurs : partout ailleurs, il
est rejeté en 401 comme tout compte sans ménage. Il ne voit aucun catalogue.
Un membre ne cumule pas les deux casquettes : rôle de plateforme **et**
claim `tenant` donnent un 403. Le rôle n'est attribué que par l'import du
realm, jamais par `Tenants.Api`. L'identifiant du ménage (= nom de schéma)
n'est exposé qu'à cet opérateur.

Conséquence assumée : le ménage est **figé dans le jeton à son émission**.
Changer une personne de ménage n'a d'effet qu'après réémission du jeton —
donc reconnexion, ou révocation de la session côté Keycloak. C'est le prix
de l'indépendance à l'exécution : `Catalogue.Api` n'appelle personne pour
savoir à quel ménage il a affaire, et continue de servir même si
`Tenants.Api` est indisponible. Ne pas « corriger » cela en ajoutant un
lookup par requête — ce serait revenir sur un choix délibéré.

Le `DbContext` résout son **schéma** par requête, à partir du tenant résolu
(service scoped). **Aucun `DbContext` ne doit pouvoir être construit sans
tenant résolu**, hors chemin d'administration explicite — concrètement,
obtenir un contexte exige de résoudre un `TenantId`, qui échoue en l'absence
de claim exploitable.

Le schéma est porté par le **`search_path` de la chaîne de connexion**, et
non déclaré dans le modèle EF (`HasDefaultSchema`). Ce point a été tranché à
l'usage, et dans ce sens précis :

- Une migration générée depuis un modèle à schéma explicite inscrit ce nom de
  schéma **en dur** dans le fichier généré. La rejouer sur un autre ménage
  créerait les tables dans le schéma d'origine — la règle « rejouer toute
  migration sur tous les schémas » devient alors inapplicable. Le modèle ne
  doit donc déclarer **aucun schéma**.
- Le `search_path` placé dans la chaîne de connexion n'est pas de l'état de
  connexion mutable : chaque chaîne a son pool, et une connexion de ce pool
  ne peut pas changer de ménage. Ce serait faux d'un `SET search_path` émis
  après l'ouverture, qui se perdrait au recyclage de la connexion. **Ne pas**
  résoudre le ménage de cette façon.

Prix assumé : **un pool par ménage**, et non un seul. Négligeable à l'échelle
d'un foyer, à réexaminer avec un grand nombre de ménages.

Le schéma `public` reste **vide** et ne contient aucune table métier : un
`search_path` mal résolu doit échouer bruyamment plutôt que de servir
silencieusement les données d'un autre ménage.

### Provisioning et migrations

C'est le coût du schéma-par-ménage, à assumer dès le départ :

- Créer un ménage = créer son schéma, y jouer toutes les migrations et
  l'inscrire dans `Tenants.Api`. Opération explicite, idempotente, pilotée
  par `Tenants.Api` (voir l'inversion d'écriture ci-dessous) : l'opérateur
  appelle `POST /households`, `Tenants.Api` inscrit le ménage à l'état
  `Provisioning`, puis demande le schéma à `Catalogue.Api`
  (`PUT /admin/households/{id}/schema`, client credentials, rôle de realm
  `provisionnement` porté par le seul compte de service de `tenants-api`),
  et ne passe le ménage `Active` qu'après confirmation. Une création
  interrompue se reprend en la rejouant. Aucun membre n'entre dans un
  ménage qui n'est pas `Active`. L'identifiant du ménage est **immuable**.
- Supprimer un ménage se fait **en deux temps**, par l'opérateur :
  1. **Archiver** (`POST /households/{id}/archive`) — réversible : tous les
     comptes Keycloak du ménage sont désactivés et leurs sessions fermées,
     le catalogue est intact. `Archiving` puis `Archived`. La restauration
     (`/restore`) ne réactive que les comptes inscrits que l'administrateur
     du ménage n'avait pas désactivés lui-même.
  2. **Purger** (`POST /households/{id}/purge`, identifiant ressaisi dans
     le corps) — irréversible, et seulement depuis `Archived` (ou une
     création abandonnée, `Provisioning`) : `Purging`, puis
     `DROP SCHEMA … CASCADE` par `Catalogue.Api`, suppression des comptes
     Keycloak du ménage, puis du registre. Une purge interrompue se reprend,
     elle ne se restaure pas. L'identifiant n'est libéré qu'à la fin.

  Limite assumée : un jeton d'accès déjà émis reste valide jusqu'à
  expiration (`accessTokenLifespan`, 5 minutes) après l'archivage. Ne pas
  « corriger » cela par une consultation du registre à chaque requête.
  Aucun export du catalogue n'est proposé avant la purge.
- La configuration du realm (clients, mappers, rôles) est **versionnée dans
  `/keycloak`** et importable. Aucune configuration cliquée à la main qui ne
  soit pas reflétée dans le dépôt.
- Toute migration EF doit être rejouée sur **tous** les schémas.
  `tools/dev.sh provision <ménage>` reste disponible en développement pour
  créer un schéma hors registre, et `tools/dev.sh migrate-all`
  itère sur les schémas existants. Ne jamais migrer un schéma à la main.
  L'opération s'arrête au premier échec, plutôt que de laisser les ménages
  dans des états différents.
- Un schéma en retard de migration est un incident : le service doit refuser
  de servir un ménage dont le schéma n'est pas à la version attendue, plutôt
  que d'échouer au hasard des requêtes.

### Administration des membres : inversion d'écriture

Le ménage est présent à deux endroits — attribut `tenant` dans Keycloak et
registre dans `Tenants.Api`. Cette duplication n'est tolérable que parce
qu'**un seul chemin écrit les deux**.

`Tenants.Api` est le **point d'entrée unique** de l'administration d'un
ménage. C'est elle qui appelle Keycloak, jamais l'inverse :

```
  Admin ──> écran d'administration (Angular)
                │
                ▼
            Tenants.Api ──> Keycloak (Admin REST API) : crée le compte,
                │                                       pose l'attribut tenant
                └─────────> sa propre base            : rattache au ménage
```

Règles qui en découlent :

- **Aucune création de membre par la console Keycloak** en usage normal. Un
  compte créé à la main n'aura pas d'entrée dans le registre et produira un
  utilisateur authentifié sans ménage exploitable — donc des 401.
- L'opération est **séquencée et réparable** : écrire dans Keycloak, puis
  dans le registre ; si la seconde étape échoue, l'opération est rejouable
  sans créer de doublon (idempotence sur le `sub`).
- Un écran d'administration doit permettre de **détecter les incohérences**
  (compte Keycloak sans ménage, entrée de registre sans compte) — pas de
  réconciliation silencieuse et automatique.
- `Tenants.Api` détient un **client confidentiel** Keycloak dédié, avec les
  seuls rôles d'administration nécessaires sur le realm. Aucun autre service
  n'a accès à l'Admin REST API.

**Pas de plugin de fédération Keycloak.** Un provider User Storage (SPI
Java) qui lirait la base de `Tenants.Api` a été envisagé et écarté : il
introduirait une chaîne de build Java dans un dépôt .NET, se casserait aux
montées de version majeures de Keycloak, et ferait de Keycloak un second
lecteur du schéma privé d'un service. L'inversion d'écriture ci-dessus donne
le même résultat — une seule interface, une seule vérité — avec du C# qu'on
écrit de toute façon.

### Tests

Au moins un test d'intégration vérifie qu'une requête authentifiée sur le
ménage A ne retourne **rien** du ménage B — y compris par recherche, par
accès direct à un identifiant, et par scan d'un code-barres déjà présent
chez B.

## Modèle de média

Le catalogue est unifié : une seule table `Medias`, une seule entité `Media`
avec un discriminant `Type` (`Livre`, `BD`, `Musique`, `Film`, `Jeu`, ...).

Le modèle est **hybride** — ne pas tout basculer d'un côté ou de l'autre :

- **En colonnes réelles**, ce qui est commun à tous les types et sert à
  filtrer, trier ou chercher : `Type`, `Titre`, `Createur` (auteur /
  réalisateur / artiste, même rôle sémantique), `Annee`, `DateAjout`,
  `Emplacement`, `Prete`.
- **En colonne `jsonb`** (`Specificites`), uniquement ce qui est propre à un
  type : ISBN et éditeur pour un livre, durée et studio pour un film, label
  et pistes pour un album.

Conséquences à respecter :

- Ajouter un type de média ne doit demander **aucune migration de schéma**.
  Si une évolution oblige à toucher la table pour un nouveau type, c'est le
  signe qu'un champ a été mis au mauvais endroit.
- PostgreSQL ne valide pas la forme du `jsonb` : celle des `Specificites`
  est garantie **côté C#**, par un type par média et une validation à
  l'entrée. Aucune écriture directe de JSON brut.
- Un champ JSON qui devient un critère de recherche fréquent est **promu en
  colonne générée indexée**, sans réécrire les données. Trois champs le sont à
  ce jour : l'**ISBN**, pour le chemin « scan », la **série** d'une BD,
  parce qu'on cherche un album par sa série bien avant son titre, et le
  **code-barres** d'un disque (stocké en EAN-13, un UPC-A y est ramené),
  pour reconnaître au scan un disque déjà possédé. La
  promotion est une migration, mais purement additive — elle ne touche aucune
  donnée déjà saisie.

  ```sql
  ALTER TABLE medias
    ADD COLUMN isbn text GENERATED ALWAYS AS (specificites ->> 'isbn') STORED;
  CREATE INDEX ix_medias_isbn ON medias (isbn);
  ```

Mapping EF Core : colonne `jsonb` et `ValueConverter` vers
`System.Text.Json`, avec **sérialisation polymorphe** (`JsonDerivedType`) —
un type C# par média derrière un `MediaSpecifics` abstrait.

`OwnsOne(...).ToJson()` a été écarté : il mappe *une* forme d'entité possédée,
alors que les spécificités en ont une par type de média. Le faire entrer dans
ce moule demanderait soit une entité possédée par type (donc une colonne par
type, ce que le modèle hybride refuse), soit un type fourre-tout aux champs
tous optionnels — qui ne valide plus rien.

Le prix assumé : on ne peut pas écrire de LINQ EF Core sur l'intérieur du
JSON. C'est précisément à quoi répond la promotion en colonne générée
ci-dessus : l'ISBN, la série et le code-barres sont déjà promus.

La politique de nommage JSON (camelCase) fait partie du **contrat de
stockage** : la colonne générée lit `specifics ->> 'isbn'`. La changer
invaliderait cette colonne sur les données déjà saisies.

**`jsonb` ne conserve pas l'ordre des clés** — il est stocké décomposé et
restitue ses clés triées par longueur puis par octets. Aucune sérialisation
ne doit donc dépendre de la position d'une clé. C'est ce qui disqualifie la
polymorphie native de `System.Text.Json` (`JsonPolymorphic`), qui exige le
discriminant en première propriété : elle passe à l'écriture et casse à la
relecture. Le discriminant est géré par `MediaSpecificsJsonConverter`, qui le
retrouve où qu'il soit, et qui sert aussi bien au stockage qu'à la frontière
HTTP — un seul mécanisme, sinon deux occasions de divergence.

Ne pas créer un service ni une table par type de média — le découpage se
fait par responsabilité métier, pas par type.

## Saisie

Deux chemins d'entrée, les deux doivent rester fonctionnels :

1. **Saisie manuelle** — formulaire, seuls le type et le titre sont requis.
2. **Scan code-barres / ISBN** — le frontend envoie le code, Catalogue.Api
   interroge Metadata.Api, pré-remplit la fiche, l'utilisateur valide ou
   corrige avant enregistrement. L'échec de l'enrichissement n'empêche
   jamais l'ajout du média.

## Conventions

### Backend (.NET)
- Code et identifiants publics en **anglais** ; les libellés affichés à
  l'utilisateur sont en français et vivent dans le frontend.
- Un dossier par feature plutôt qu'un découpage par couche technique.
- DTOs explicites aux frontières HTTP : jamais d'entité EF exposée
  directement.
- `nullable` et warnings-as-errors activés.
- Nommage en base en `snake_case` (`EFCore.NamingConventions`) : PostgreSQL
  replie les identifiants non cités en minuscules, le PascalCase d'EF
  obligerait à citer chaque identifiant à la main en SQL.
- Configuration par `IOptions<T>`, aucun secret en dur dans le code.
- Versions de paquets **gérées centralement** dans `Directory.Packages.props`
  (`ManagePackageVersionsCentrally`, épinglage transitif activé). Aucune
  version dans un `.csproj`, et **aucune version flottante** : avec plusieurs
  services partageant EF Core, un `10.0.*` dans un projet suffit à casser la
  compilation d'un autre en CS1705.

### Frontend (Angular)
- Composants standalone, pas de NgModules.
- État via les signals ; le state global uniquement s'il est réellement
  partagé.
- Un service Angular par Web API, généré ou typé depuis les contrats.
- Pas de `any`.

### Général
- **Ne pas s'occuper de git** : ni commit, ni branche, ni push, et ne pas
  le proposer en fin de tâche. Le développeur gère git lui-même ; les
  modifications restent dans l'arbre de travail.
- Messages de commit en français, à l'impératif (pour le développeur).
- Tests : xUnit côté .NET, Vitest/Jest côté Angular. Toute règle métier
  non triviale est testée.

### Accès depuis un smartphone
L'application doit rester utilisable **depuis l'IP du Mac sur le réseau
local**, pour être essayée sur un smartphone (caméra comprise pour le
scan). Le chemin est le relais HTTPS du frontend, `tools/dev.sh front-lan`
(port 4443) : le téléphone ne joint que cette origine, qui relaie les API et
Keycloak.

Toute évolution doit préserver ce chemin :

- Une nouvelle Web API appelée par le frontend reçoit une entrée dans
  `frontend/proxy.lan.json` et une URL relative dans `relayedSettings`
  (`frontend/src/app/core/app-settings.ts`), comme `/api` et
  `/tenants-api`.
- Elle accepte en plus l'issuer vu par le téléphone : sa commande
  `tools/dev.sh` appelle `lan_issuer`, qui le fournit par l'environnement.
  Jamais d'adresse IP en dur dans le dépôt.
- Une fonctionnalité n'est pas terminée tant qu'elle ne fonctionne pas
  aussi par ce relais.

## Commandes

À compléter au fur et à mesure de la mise en place du projet.

```bash
# Infrastructure locale (PostgreSQL + Keycloak)
docker compose -f docker/docker-compose.yml up -d

# Réimporter la configuration du realm après modification
docker compose -f docker/docker-compose.yml restart keycloak

# Un service .NET
dotnet run --project services/Catalogue.Api
tools/dev.sh metadata            # Metadata.Api sur :5081
tools/dev.sh metadata-migrate    # migrations du cache (base metadata, non partitionnée)
tools/dev.sh tenants             # Tenants.Api sur :5082
tools/dev.sh tenants-migrate     # migrations du registre + inscription des comptes de dev
dotnet ef database update --project services/Catalogue.Api
dotnet test

# Frontend
cd frontend && npm start
```

## À demander avant de décider seul

- Ajout d'un nouveau microservice ou d'une nouvelle base.
- Introduction d'un bus de messages ou d'une communication asynchrone.
- Ajout d'une API externe payante ou nécessitant une clé.
- Tout changement de schéma qui casse les données déjà saisies.
- Passage à un modèle où une personne appartient à plusieurs ménages.
- Abandon du schéma-par-ménage au profit d'une colonne `TenantId` partagée.
- Toute exposition du `TenantId` ou du nom de schéma côté client.
- Passage à un realm Keycloak par ménage.
- Ajout d'un plugin Keycloak (User Storage SPI ou autre extension Java) :
  écarté par décision d'architecture, voir « inversion d'écriture ».
- Passage à une résolution du ménage par appel à `Tenants.Api` au lieu du
  claim du jeton.
- Stockage d'identifiants de connexion ailleurs que dans Keycloak.
- Désactivation d'une validation de jeton, même temporaire, même en dev.
