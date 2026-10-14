#!/usr/bin/env bash
#
# Commandes de développement. Tout passe par ici : la sortie est tracée dans
# tools/last-run.log, qui est le fichier à relire après un échec.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG="$ROOT/tools/last-run.log"
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
  migrate-all     Applique les migrations à tous les schémas de ménage
  versions        Affiche les versions des outils installés
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

: > "$LOG"
echo "=== $(date '+%Y-%m-%d %H:%M:%S') — tools/dev.sh ${*:-(sans argument)}" >> "$LOG"

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
  versions)
    run dotnet --version
    run docker --version
    run node --version
    ;;
  *) usage; exit 2 ;;
esac
