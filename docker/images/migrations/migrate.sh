#!/usr/bin/env bash
# Applique toutes les migrations EF, dans l'ordre, et s'arrête au premier
# échec. Idempotent : une base déjà à jour n'est pas touchée.
#
# Environnement attendu :
#   DB_HOST                         hôte PostgreSQL
#   METADATA_DB_PASSWORD, TENANTS_DB_PASSWORD, CATALOGUE_DB_PASSWORD
#
# Chaque base est migrée avec le rôle qui la possède, et seulement lui.
set -euo pipefail

connection() { # base, rôle, mot de passe
  printf 'Host=%s;Port=5432;Database=%s;Username=%s;Password=%s' "$DB_HOST" "$1" "$2" "$3"
}

echo "--- metadata"
/migrations/metadata-migrations --connection "$(connection metadata metadata "$METADATA_DB_PASSWORD")"

echo "--- tenants"
/migrations/tenants-migrations --connection "$(connection tenants tenants "$TENANTS_DB_PASSWORD")"

# Un schéma par ménage : la même migration est rejouée sur chacun, sous
# son search_path (voir CatalogueDbContextDesignTimeFactory). Aucun ménage
# à la livraison : la boucle ne fait alors rien, et le provisionnement
# migrera chaque nouveau schéma à sa création.
households=$(PGPASSWORD="$CATALOGUE_DB_PASSWORD" psql -h "$DB_HOST" -U catalogue -d catalogue -At -v ON_ERROR_STOP=1 -c \
  "select schema_name from information_schema.schemata
    where schema_name not in ('public', 'information_schema')
      and schema_name not like 'pg\_%'
    order by schema_name")

for household in $households; do
  echo "--- catalogue : ménage $household"
  CATALOGUE_TENANT="$household" \
  CATALOGUE_CONNECTION="$(connection catalogue catalogue "$CATALOGUE_DB_PASSWORD")" \
    /migrations/catalogue-migrations
done

echo "--- migrations terminées"
