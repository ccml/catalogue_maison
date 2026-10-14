-- Développement local uniquement.
--
-- La base `catalogue` est créée par POSTGRES_DB ; ce script y ajoute un
-- schéma par ménage, puis les bases des deux autres services.

-- Deux ménages de développement, pour que le test d'isolation ait de quoi
-- mordre. En usage normal ces schémas sont créés par le provisioning.
CREATE SCHEMA IF NOT EXISTS martin AUTHORIZATION maison;
CREATE SCHEMA IF NOT EXISTS durand AUTHORIZATION maison;

-- `public` n'est jamais le schéma d'un ménage : un search_path mal résolu
-- doit échouer bruyamment, pas retomber sur des tables qui existent.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Registre des ménages : base dédiée, non partitionnée.
CREATE DATABASE tenants OWNER maison;

-- Cache d'enrichissement, tenant-agnostique et volontairement partagé.
CREATE DATABASE metadata OWNER maison;
