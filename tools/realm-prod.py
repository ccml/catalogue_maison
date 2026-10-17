#!/usr/bin/env python3
"""Dérive le realm de production du realm versionné dans /keycloak.

Usage : realm-prod.py <realm-maison.json> <sortie.json>

Un seul export est maintenu à la main, celui de développement : le realm de
production en est tiré à la construction de l'image Keycloak, pour que les
deux ne divergent jamais. Ce qui change :

- Aucun ménage : les comptes de développement (alice, theo, bruno,
  orphelin…) disparaissent. Restent les comptes de service, et l'opérateur,
  dont le mot de passe initial vient de l'environnement et doit être changé
  à la première connexion.
- Aucun secret en clair : les secrets de clients deviennent des variables
  d'environnement, que Keycloak substitue à l'import (`${VARIABLE}`).
- Aucune adresse de développement dans les URI de redirection : seule reste
  l'URI relative `/*`, que Keycloak résout contre son nom d'hôte public.
- Protection contre les attaques par force brute activée.

L'import n'a lieu qu'au premier démarrage, sur une base vide : changer ce
fichier ensuite ne modifie pas un realm déjà en place.
"""

import json
import sys

OPERATOR = "operateur"
OPERATOR_PASSWORD = "${CATALOGUE_OPERATOR_PASSWORD}"

CLIENT_SECRETS = {
    "catalogue-api": "${CATALOGUE_KC_SECRET_CATALOGUE_API}",
    "metadata-api": "${CATALOGUE_KC_SECRET_METADATA_API}",
    "tenants-api": "${CATALOGUE_KC_SECRET_TENANTS_API}",
}


def derive(realm: dict) -> dict:
    users = []
    for user in realm.get("users", []):
        if "serviceAccountClientId" in user:
            users.append(user)
        elif user["username"] == OPERATOR:
            if user.get("attributes", {}).get("tenant"):
                raise SystemExit("L'opérateur ne doit appartenir à aucun ménage.")
            user["credentials"] = [
                {"type": "password", "value": OPERATOR_PASSWORD, "temporary": True}
            ]
            # L'import ignore « temporary » : c'est l'action requise qui
            # impose de changer le mot de passe à la première connexion.
            user["requiredActions"] = ["UPDATE_PASSWORD"]
            users.append(user)

    if not any(u["username"] == OPERATOR for u in users):
        raise SystemExit(f"Compte « {OPERATOR} » absent du realm source.")
    realm["users"] = users

    for client in realm.get("clients", []):
        client_id = client["clientId"]
        if "secret" in client:
            if client_id not in CLIENT_SECRETS:
                raise SystemExit(f"Secret du client « {client_id} » sans variable de production.")
            client["secret"] = CLIENT_SECRETS[client_id]
        if client_id == "catalogue-frontend":
            client["redirectUris"] = ["/*"]
            client["webOrigins"] = ["+"]

    realm["bruteForceProtected"] = True
    return realm


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    with open(sys.argv[1], encoding="utf-8") as source:
        realm = json.load(source)
    with open(sys.argv[2], "w", encoding="utf-8") as target:
        json.dump(derive(realm), target, ensure_ascii=False, indent=2)
        target.write("\n")


if __name__ == "__main__":
    main()
