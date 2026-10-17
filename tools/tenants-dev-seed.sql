-- Développement local uniquement : inscrit au registre de Tenants.Api les
-- ménages et les comptes que l'import du realm crée dans Keycloak.
--
-- Les identifiants sont ceux fixés dans keycloak/realm-maison.json : le
-- registre est indexé par le sub Keycloak. Idempotent.
--
-- `orphelin` n'est volontairement pas inscrit : il n'a pas de ménage.

INSERT INTO households (id, display_name, created_at) VALUES
  ('martin', 'Martin', now()),
  ('durand', 'Durand', now())
ON CONFLICT (id) DO NOTHING;

INSERT INTO members (subject, household_id, username, registered_at) VALUES
  ('0a11ce00-0000-4000-8000-000000000001', 'martin', 'alice', now()),
  ('0a11ce00-0000-4000-8000-000000000002', 'martin', 'theo',  now()),
  ('0a11ce00-0000-4000-8000-000000000003', 'durand', 'bruno', now())
ON CONFLICT (subject) DO NOTHING;
