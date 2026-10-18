#!/usr/bin/env bash
# Sauvegarde des bases de CatalogueMaison, à lancer par cron sur le serveur.
#
# Un dossier par jour, dumps/AAAA-MM-JJ/ :
#   globals.sql.gz   rôles et leurs mots de passe (pg_dumpall --globals-only)
#   <base>.dump      une archive pg_dump au format custom par base :
#                    catalogue (un schéma par ménage), tenants, keycloak,
#                    metadata. Le format custom permet de restaurer une seule
#                    base, voire un seul schéma de ménage (pg_restore -n).
#
# Export par pg_dump, jamais par copie de cataloguemaison-db/data à chaud.
# Chaque base est cohérente en elle-même ; deux bases sont exportées à
# quelques secondes d'écart (un ménage créé pendant la sauvegarde peut
# figurer dans l'une et pas dans l'autre).
#
# Le jour en cours n'est publié (renommage du dossier temporaire) que si
# toutes les archives ont été relues par pg_restore --list. Sortie non nulle
# au moindre échec : cron l'envoie par mail si MAILTO est défini.
#
# Rotation : les KEEP_DAYS derniers jours, plus la sauvegarde du 1er de
# chaque mois pendant KEEP_MONTHS mois.
#
# Le fichier .env n'est PAS sauvegardé ici : il contient les secrets, et il
# faut le conserver à part, en lieu sûr — sans lui, une restauration sur un
# serveur neuf ne retrouve pas les mots de passe attendus par les services.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

DB_CONTAINER="${DB_CONTAINER:-cataloguemaison-db}"
DB_SUPERUSER="${DB_SUPERUSER:-postgres}"
BACKUP_DIR="${BACKUP_DIR:-$SCRIPT_DIR/dumps}"
KEEP_DAYS="${KEEP_DAYS:-14}"
KEEP_MONTHS="${KEEP_MONTHS:-12}"

# Les dumps contiennent les empreintes de mots de passe (rôles, comptes
# Keycloak) : lisibles par le seul propriétaire.
umask 077

log() { printf '%s %s\n' "$(date '+%F %T')" "$*"; }
fail() { log "ÉCHEC : $*" >&2; exit 1; }

mkdir -p "$BACKUP_DIR"

# Une seule sauvegarde à la fois (flock existe sur le serveur Linux ; sans
# lui, on s'en passe).
if command -v flock >/dev/null 2>&1; then
  exec 9>"$BACKUP_DIR/.lock"
  flock -n 9 || fail "une sauvegarde est déjà en cours"
fi

docker exec "$DB_CONTAINER" pg_isready -q -U "$DB_SUPERUSER" \
  || fail "la base $DB_CONTAINER ne répond pas"

day="$(date +%F)"
tmp="$BACKUP_DIR/.tmp-$day"
rm -rf "$tmp"
trap 'rm -rf "$tmp"' EXIT
mkdir "$tmp"

log "Sauvegarde de $DB_CONTAINER vers $BACKUP_DIR/$day"

docker exec "$DB_CONTAINER" pg_dumpall -U "$DB_SUPERUSER" --globals-only \
  | gzip > "$tmp/globals.sql.gz"
# pipefail : un échec de pg_dumpall fait échouer la ligne, malgré gzip.

# Toutes les bases applicatives, découvertes plutôt que listées : une base
# ajoutée plus tard est sauvegardée sans toucher à ce script.
databases="$(docker exec "$DB_CONTAINER" psql -U "$DB_SUPERUSER" -d postgres -Atc \
  "SELECT datname FROM pg_database WHERE NOT datistemplate AND datname <> 'postgres' ORDER BY datname")"
[ -n "$databases" ] || fail "aucune base à sauvegarder"

for db in $databases; do
  docker exec "$DB_CONTAINER" pg_dump -U "$DB_SUPERUSER" -Fc -d "$db" > "$tmp/$db.dump"
  # Relecture : une archive tronquée ou corrompue échoue ici, pas le jour
  # de la restauration.
  docker exec -i "$DB_CONTAINER" pg_restore -l < "$tmp/$db.dump" > /dev/null \
    || fail "archive illisible : $db"
  log "  $db : $(du -h "$tmp/$db.dump" | cut -f1)"
done

gzip -t "$tmp/globals.sql.gz" || fail "globals.sql.gz illisible"

# Publication : une sauvegarde relancée le même jour remplace la précédente.
rm -rf "${BACKUP_DIR:?}/$day"
mv "$tmp" "$BACKUP_DIR/$day"
trap - EXIT

# Rotation, sur le seul motif des dossiers de ce script.
find "$BACKUP_DIR" -mindepth 1 -maxdepth 1 -type d -name '????-??-??' \
  ! -name '*-01' -mtime +"$KEEP_DAYS" -exec rm -rf {} +
find "$BACKUP_DIR" -mindepth 1 -maxdepth 1 -type d -name '????-??-01' \
  -mtime +"$((KEEP_MONTHS * 31))" -exec rm -rf {} +

log "Terminé : $(du -sh "$BACKUP_DIR/$day" | cut -f1)"
