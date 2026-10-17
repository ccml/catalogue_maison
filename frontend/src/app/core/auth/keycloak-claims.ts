/**
 * Claims que ce frontend lit dans le jeton d'identité.
 *
 * Ils servent **uniquement à l'affichage** : masquer un lien n'est pas une
 * règle de sécurité. Toute autorisation réelle est revalidée par l'API, qui
 * ne fait confiance à rien de ce qui vient d'ici.
 */
export interface KeycloakClaims {
  readonly sub?: string;
  readonly preferred_username?: string;
  readonly name?: string;
  /** Ménage de l'utilisateur. Jamais renvoyé à l'API : elle le lit elle-même. */
  readonly tenant?: string;
  readonly realm_access?: {
    readonly roles?: readonly string[];
  };
}

export const Roles = {
  adult: 'adulte',
  child: 'enfant',
  householdAdmin: 'admin-menage',
  platformAdmin: 'admin-plateforme',
} as const;

/**
 * Ménage porté par le jeton, ou `null`.
 *
 * Seule l'absence de valeur est jugée ici. La forme exacte, elle, est
 * validée par l'API : la dupliquer des deux côtés les ferait dériver, et un
 * ménage mal formé est de toute façon refusé à la première requête.
 */
export function householdOf(claims: KeycloakClaims | null): string | null {
  const tenant = claims?.tenant?.trim();
  return tenant === undefined || tenant.length === 0 ? null : tenant;
}

export function rolesOf(claims: KeycloakClaims | null): readonly string[] {
  return claims?.realm_access?.roles ?? [];
}

/**
 * Vrai si l'interface peut proposer de consulter un catalogue.
 *
 * Sans ménage, il n'y a aucun catalogue à montrer : proposer d'y naviguer
 * mène à un écran qui refuse, ce qui donne l'impression d'une panne plutôt
 * que d'un compte à rattacher.
 */
export function hasHousehold(claims: KeycloakClaims | null): boolean {
  return householdOf(claims) !== null;
}

/**
 * Vrai si l'interface peut proposer d'écrire. L'API tranche vraiment.
 *
 * Le rôle ne suffit pas : écrire suppose un ménage où écrire.
 */
export function mayWrite(claims: KeycloakClaims | null): boolean {
  if (!hasHousehold(claims)) {
    return false;
  }

  const roles = rolesOf(claims);
  return roles.includes(Roles.adult) || roles.includes(Roles.householdAdmin);
}

/**
 * Vrai si l'interface peut proposer l'administration des membres du ménage.
 * Tenants.Api revérifie le rôle et le ménage à chaque requête.
 */
export function mayAdministerHousehold(claims: KeycloakClaims | null): boolean {
  return hasHousehold(claims) && rolesOf(claims).includes(Roles.householdAdmin);
}

/**
 * Vrai si l'interface peut proposer l'administration des ménages eux-mêmes.
 *
 * Réservée à l'opérateur : un compte **sans** ménage. Un membre qui porterait
 * aussi le rôle est refusé par Tenants.Api ; l'interface ne le lui propose
 * donc pas non plus.
 */
export function mayAdministerPlatform(claims: KeycloakClaims | null): boolean {
  return !hasHousehold(claims) && rolesOf(claims).includes(Roles.platformAdmin);
}
