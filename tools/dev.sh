#!/usr/bin/env bash
#
# Commandes de développement. Tout passe par ici : chaque commande trace sa
# sortie dans tools/logs/<commande>.log, qui est le fichier à relire après un
# échec.
#
# Un journal par commande, et non un seul : lancer « all » puis « front » ne
# doit pas effacer le résultat du premier, et chaque fichier ne contient que
# la dernière exécution de sa commande — rien à purger.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMMANDE="${1:-sans-argument}"
LOGS="$ROOT/tools/logs"
LOG="$LOGS/$COMMANDE.log"
COMPOSE="docker compose -f $ROOT/docker/docker-compose.yml"
SOLUTION="$ROOT/CatalogueMaison.slnx"
API="$ROOT/services/Catalogue.Api"

usage() {
  cat <<'USAGE'
Usage: tools/dev.sh <commande>

  up              Lance PostgreSQL et Keycloak
  down            Arrête l'infrastructure
  reset           Détruit l'infrastructure et ses volumes, puis relance
  restore         Restaure les paquets NuGet
  build           Compile la solution
  test            Lance les tests (nécessite Docker : Testcontainers)
  all             restore, puis build, puis test
  migration <nom> Génère une migration EF
  provision <nom> Crée le schéma d'un ménage et y applique les migrations
  migrate-all     Applique les migrations à tous les schémas de ménage
  keycloak-reimport  Recrée Keycloak pour réimporter le realm
  api             Démarre Catalogue.Api sur http://localhost:5080
  front           Compile le frontend et lance ses tests
  front-start     Démarre le frontend en développement
  front-install   Installe les dépendances du frontend
  versions        Affiche les versions des outils installés

La sortie de chaque commande est tracée dans tools/logs/<commande>.log.
USAGE
}

run() {
  echo "--- \$ $*" | tee -a "$LOG"
  "$@" 2>&1 | tee -a "$LOG"
  return "${PIPESTATUS[0]}"
}

# Les schémas de ménage, lus dans PostgreSQL en attendant que Tenants.Api
# tienne le registre. À remplacer par un appel au registre en phase 4.
households() {
  $COMPOSE exec -T postgres psql -U maison -d catalogue -At -c \
    "select schema_name from information_schema.schemata
      where schema_name not in ('public','information_schema')
        and schema_name not like 'pg_%'
      order by schema_name"
}

mkdir -p "$LOGS"
echo "=== $(date '+%Y-%m-%d %H:%M:%S') — tools/dev.sh ${*:-(sans argument)}" > "$LOG"

case "${1:-}" in
  up)      run $COMPOSE up -d ;;
  down)    run $COMPOSE down ;;
  reset)   run $COMPOSE down -v && run $COMPOSE up -d ;;
  restore) run dotnet restore "$SOLUTION" ;;
  build)   run dotnet build "$SOLUTION" --no-restore ;;
  test)    run dotnet test "$SOLUTION" --no-build --logger "console;verbosity=normal" ;;
  all)
    run dotnet restore "$SOLUTION" \
      && run dotnet build "$SOLUTION" --no-restore \
      && run dotnet test "$SOLUTION" --no-build --logger "console;verbosity=normal"
    ;;
  migration)
    [ -n "${2:-}" ] || { echo "Nom de migration manquant." | tee -a "$LOG"; exit 2; }
    run dotnet ef migrations add "$2" --project "$API" --output-dir Infrastructure/Persistence/Migrations
    ;;
  provision)
    # Créer un ménage = créer son schéma, puis y jouer toutes les migrations.
    # Idempotent : rejouable sans effet de bord.
    [ -n "${2:-}" ] || { echo "Nom de ménage manquant." | tee -a "$LOG"; exit 2; }
    case "$2" in
      [a-z][a-z0-9_]*) : ;;
      *) echo "Nom de ménage invalide : « $2 » (minuscules, chiffres, underscore)." | tee -a "$LOG"; exit 2 ;;
    esac
    run $COMPOSE exec -T postgres psql -U maison -d catalogue -v ON_ERROR_STOP=1 \
        -c "create schema if not exists \"$2\" authorization maison" \
      && CATALOGUE_TENANT="$2" run dotnet ef database update --project "$API"
    ;;
  migrate-all)
    # Une migration en retard est un incident : on s'arrête au premier échec
    # plutôt que de laisser des schémas dans des états différents.
    status=0
    while read -r tenant; do
      [ -n "$tenant" ] || continue
      echo "--- ménage : $tenant" | tee -a "$LOG"
      CATALOGUE_TENANT="$tenant" run dotnet ef database update --project "$API" || { status=1; break; }
    done < <(households)
    exit "$status"
    ;;
  keycloak-reimport)
    # Un restart ne réimporte pas un realm déjà présent : il faut recréer le
    # conteneur. Keycloak tourne en start-dev, sur une base en mémoire.
    run $COMPOSE rm -sf keycloak && run $COMPOSE up -d keycloak
    ;;
  api)           run dotnet run --project "$API" ;;
  front-install) run bash -c "cd '$ROOT/frontend' && npm install" ;;
  front-start)
    # ng serve lit le clavier (« press h + enter ») : lancé en arrière-plan
    # depuis un terminal, il serait suspendu par SIGTTIN et resterait à
    # l'écoute sans jamais répondre. On lui coupe donc l'entrée standard.
    run bash -c "cd '$ROOT/frontend' && npm start < /dev/null"
    ;;
  front-build)   run bash -c "cd '$ROOT/frontend' && npm run build" ;;
  front-test)    run bash -c "cd '$ROOT/frontend' && npm run test -- --watch=false" ;;
  front)
    run bash -c "cd '$ROOT/frontend' && npm run build" \
      && run bash -c "cd '$ROOT/frontend' && npm run test -- --watch=false"
    ;;
  versions)
    run dotnet --version
    run docker --version
    run node --version
    ;;
  *) usage; exit 2 ;;
esac
