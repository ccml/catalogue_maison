# Keycloak de production : base PostgreSQL, thème du site et realm de
# production intégrés, configuration figée à la construction (--optimized).

# Le realm de production est dérivé de l'export versionné de /keycloak :
# opérateur seul, aucun ménage, secrets lus dans l'environnement.
FROM python:3.13-alpine AS realm
COPY keycloak/realm-maison.json tools/realm-prod.py /src/
RUN mkdir /out && python3 -I /src/realm-prod.py /src/realm-maison.json /out/realm-maison.json

FROM quay.io/keycloak/keycloak:26.4 AS builder
ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true
COPY keycloak/themes/ /opt/keycloak/themes/
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.4
COPY --from=builder /opt/keycloak/ /opt/keycloak/
COPY --from=realm /out/realm-maison.json /opt/keycloak/data/import/realm-maison.json

# L'import n'a lieu que si le realm n'existe pas encore : au premier
# démarrage seulement. Ensuite, la base fait foi.
ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
CMD ["start", "--optimized", "--import-realm"]
