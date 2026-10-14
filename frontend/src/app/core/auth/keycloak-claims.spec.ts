import { describe, expect, it } from 'vitest';
import {
  Roles,
  hasHousehold,
  householdOf,
  mayWrite,
  rolesOf,
  type KeycloakClaims,
} from './keycloak-claims';

const alice: KeycloakClaims = {
  preferred_username: 'alice',
  tenant: 'martin',
  realm_access: { roles: [Roles.adult, Roles.householdAdmin] },
};

const theo: KeycloakClaims = {
  preferred_username: 'theo',
  tenant: 'martin',
  realm_access: { roles: [Roles.child] },
};

/** Authentifié, avec un rôle, mais rattaché à aucun ménage. */
const orphelin: KeycloakClaims = {
  preferred_username: 'orphelin',
  realm_access: { roles: [Roles.adult] },
};

describe('règles d’affichage tirées des claims', () => {
  it('reconnaît le ménage, et son absence', () => {
    expect(householdOf(alice)).toBe('martin');
    expect(householdOf(orphelin)).toBeNull();
    expect(householdOf(null)).toBeNull();
    expect(householdOf({ tenant: '   ' })).toBeNull();
  });

  it("n'offre aucune navigation sans ménage", () => {
    // Sans ménage il n'y a aucun catalogue : proposer d'y aller donne
    // l'impression d'une panne plutôt que d'un compte à rattacher.
    expect(hasHousehold(alice)).toBe(true);
    expect(hasHousehold(orphelin)).toBe(false);
  });

  it("n'offre pas d'écrire sans ménage, même avec le rôle adulte", () => {
    // Le piège : orphelin a bien le rôle adulte. Le rôle ne suffit pas,
    // écrire suppose un ménage où écrire.
    expect(rolesOf(orphelin)).toContain(Roles.adult);
    expect(mayWrite(orphelin)).toBe(false);
  });

  it("n'offre d'écrire qu'aux adultes et aux administrateurs du ménage", () => {
    expect(mayWrite(alice)).toBe(true);
    expect(mayWrite(theo)).toBe(false);
  });

  it('ne suppose aucun rôle quand le jeton n’en porte pas', () => {
    expect(rolesOf({ tenant: 'martin' })).toEqual([]);
    expect(mayWrite({ tenant: 'martin' })).toBe(false);
  });
});
