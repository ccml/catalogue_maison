# Conteneur éphémère de migrations, lancé avant les services à chaque
# déploiement : bases metadata et tenants, puis chaque schéma de ménage de
# la base catalogue. S'arrête au premier échec — les services, qui en
# dépendent, ne démarrent alors pas, plutôt que de servir des ménages dont
# le schéma n'est pas à la version attendue.
#
# Les migrations sont empaquetées en « bundles » EF Core autonomes : pas de
# SDK .NET dans l'image.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# Même version que les paquets EF Core de Directory.Packages.props.
ARG EF_VERSION=10.0.13
RUN dotnet tool install --global dotnet-ef --version ${EF_VERSION}
ENV PATH="$PATH:/root/.dotnet/tools"
WORKDIR /src

COPY Directory.Packages.props ./
COPY services/Catalogue.Api/ services/Catalogue.Api/
COPY services/Metadata.Api/ services/Metadata.Api/
COPY services/Tenants.Api/ services/Tenants.Api/

RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) exit 1 ;; esac \
    && for project in Catalogue Metadata Tenants; do \
         # Le bundle ne restaure pas lui-même les paquets.
         dotnet restore services/$project.Api/$project.Api.csproj -r $rid || exit 1; \
         dotnet ef migrations bundle --project services/$project.Api/$project.Api.csproj \
           --configuration Release --target-runtime $rid --self-contained \
           --output /out/$(echo $project | tr '[:upper:]' '[:lower:]')-migrations || exit 1; \
       done

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/ /migrations/
COPY docker/images/migrations/migrate.sh /migrations/migrate.sh
USER $APP_UID
ENTRYPOINT ["/bin/bash", "/migrations/migrate.sh"]
