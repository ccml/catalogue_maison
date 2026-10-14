import { HttpErrorResponse } from '@angular/common/http';

/**
 * Traduit une erreur HTTP en phrase affichable.
 *
 * Chaque cas dit ce qui s'est passé et ce qu'il y a à faire — un message
 * vague renvoie l'utilisateur vers quelqu'un d'autre sans rien lui apprendre.
 */
export function describeHttpError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return "Le catalogue n'a pas répondu comme prévu.";
  }

  switch (error.status) {
    case 0:
      return "Le catalogue est injoignable. Vérifiez qu'il est démarré.";
    case 401:
      return 'Votre session a expiré. Reconnectez-vous pour continuer.';
    case 403:
      return "Votre compte n'a pas le droit de faire cela.";
    case 404:
      return 'Introuvable dans le catalogue du ménage.';
    case 503:
      return "Le catalogue du ménage n'est pas à jour. Prévenez la personne qui l'administre.";
    default:
      return `Le catalogue a répondu une erreur ${error.status}.`;
  }
}
