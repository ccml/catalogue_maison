# Image d'une Web API .NET : Catalogue.Api, Metadata.Api ou Tenants.Api.
#
#   docker buildx build -f docker/images/service.Dockerfile \
#     --build-arg PROJECT=Catalogue.Api .
#
# Construite depuis la racine du dépôt (Directory.Packages.props y vit), par
# `tools/dev.sh images`. La compilation tourne sur l'architecture du poste et
# vise celle de l'image : pas d'émulation pour construire une image amd64
# depuis un Mac ARM.

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
ARG TARGETARCH
WORKDIR /src

COPY Directory.Packages.props ./
COPY services/${PROJECT}/${PROJECT}.csproj services/${PROJECT}/
RUN dotnet restore services/${PROJECT}/${PROJECT}.csproj -a $TARGETARCH

COPY services/${PROJECT}/ services/${PROJECT}/
RUN dotnet publish services/${PROJECT}/${PROJECT}.csproj -c Release -a $TARGETARCH \
      --no-restore -o /app \
    # Les réglages de développement (secrets de dev, Keycloak local) n'ont
    # rien à faire dans une image de production.
    && rm -f /app/appsettings.Development.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
WORKDIR /app
COPY --from=build /app ./

# Utilisateur non privilégié fourni par l'image, port 8080 par défaut.
USER $APP_UID
ENV ASPNETCORE_ENVIRONMENT=Production \
    APP_DLL=${PROJECT}.dll
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
