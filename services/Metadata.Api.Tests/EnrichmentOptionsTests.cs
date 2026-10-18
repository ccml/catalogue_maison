using Metadata.Api.Infrastructure.Sources;
using Xunit;

namespace Metadata.Api.Tests;

/// <summary>
/// Le User-Agent exigé par MusicBrainz vient de la configuration du
/// déploiement : une valeur mal formée doit être refusée au démarrage, pas à
/// chaque requête.
/// </summary>
public sealed class EnrichmentOptionsTests
{
    [Theory]
    [InlineData("CatalogueMaison/1.0 (admin@exemple.be)")]
    [InlineData("CatalogueMaison/0.1 (developpement local)")]
    [InlineData("CatalogueMaison/1.0")]
    public void Un_user_agent_qui_nomme_l_application_est_accepte(string value) =>
        Assert.True(EnrichmentOptions.IsValidUserAgent(value));

    [Theory]
    [InlineData("admin@exemple.be")]
    [InlineData("(admin@exemple.be)")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Une_adresse_seule_ou_une_valeur_vide_est_refusee(string? value) =>
        Assert.False(EnrichmentOptions.IsValidUserAgent(value));
}
