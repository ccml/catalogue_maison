# Frontend Angular servi par nginx, qui relaie aussi les API et Keycloak
# sous la même origine : /api, /tenants-api, /realms, /resources. C'est la
# seule porte d'entrée de CatalogueMaison ; le reverse proxy, devant, ne
# fait que le TLS.

FROM --platform=$BUILDPLATFORM node:24-alpine AS build
WORKDIR /src
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build -- --configuration production
# nginx tourne en non privilégié : un fichier lisible du seul propriétaire
# (un 600 venu du poste de travail, comme les icônes) y donnerait un 403.
RUN chmod -R a+rX dist/frontend/browser

# Variante non privilégiée de l'image officielle : écoute sur 8080.
FROM nginxinc/nginx-unprivileged:1.29-alpine
COPY docker/images/nginx/cataloguemaison.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/frontend/browser/ /usr/share/nginx/html/
EXPOSE 8080
