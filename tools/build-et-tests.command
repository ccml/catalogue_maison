#!/usr/bin/env bash
# Double-cliquable depuis le Finder : restaure, compile, lance les tests.
# La sortie complète est écrite dans tools/logs/all.log.
cd "$(dirname "$0")/.." || exit 1
./tools/dev.sh all
code=$?
echo
if [ "$code" -eq 0 ]; then
  echo "=== Terminé sans erreur."
else
  echo "=== Échec (code $code) — voir tools/logs/all.log"
fi
echo "Cette fenêtre peut être fermée."
