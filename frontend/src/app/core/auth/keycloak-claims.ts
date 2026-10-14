/**
 * Claims que ce frontend lit dans le jeton.
 *
 * Ils servent **uniquement à l'affichage** : masquer un bouton n'est pas une
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
} as const;
