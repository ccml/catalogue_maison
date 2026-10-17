using System.Net;
using System.Text;
using Catalogue.Api.Infrastructure;
using Catalogue.Api.Infrastructure.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Catalogue.Api.Tests;

/// <summary>
/// Toute défaillance de Metadata.Api doit devenir un résultat
/// « indisponible », jamais une exception : l'échec de l'enrichissement
/// n'empêche jamais l'ajout d'un média.
/// </summary>
public sealed class MetadataClientTests
{
    private static MetadataClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://metadata.test/") },
            NullLogger<MetadataClient>.Instance);

    [Fact]
    public async Task Une_notice_est_lue_depuis_le_contrat_de_Metadata_Api()
    {
        const string body = """
            {"code":"9782205059250","kind":"Isbn","record":{"kind":"ComicBook","title":"Ame rouge",
             "creators":["Juan Díaz Canales"],"series":"Blacksad","volume":3,"tracks":[],"source":"bnf"}}
            """;
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        var lookup = await client.LookupAsync("9782205059250", CancellationToken.None);

        Assert.Equal(MetadataLookupOutcome.Found, lookup.Outcome);
        Assert.Equal("ComicBook", lookup.Record!.Kind);
        Assert.Equal("Blacksad", lookup.Record.Series);
        Assert.Equal(3, lookup.Record.Volume);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, MetadataLookupOutcome.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, MetadataLookupOutcome.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable, MetadataLookupOutcome.Unavailable)]
    [InlineData(HttpStatusCode.Unauthorized, MetadataLookupOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, MetadataLookupOutcome.Unavailable)]
    public async Task Chaque_statut_a_son_issue(HttpStatusCode status, MetadataLookupOutcome expected)
    {
        var client = Client(_ => new HttpResponseMessage(status));

        var lookup = await client.LookupAsync("9782205059250", CancellationToken.None);

        Assert.Equal(expected, lookup.Outcome);
    }

    [Fact]
    public async Task Un_service_injoignable_est_indisponible()
    {
        var client = Client(_ => throw new HttpRequestException("Connexion refusée"));

        Assert.Equal(MetadataLookupOutcome.Unavailable,
            (await client.LookupAsync("9782205059250", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Un_delai_depasse_est_indisponible()
    {
        var client = Client(_ => throw new TaskCanceledException("Délai dépassé"));

        Assert.Equal(MetadataLookupOutcome.Unavailable,
            (await client.LookupAsync("9782205059250", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Une_reponse_illisible_est_indisponible()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>", Encoding.UTF8, "application/json")
        });

        Assert.Equal(MetadataLookupOutcome.Unavailable,
            (await client.LookupAsync("9782205059250", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Le_jeton_de_service_est_reutilise_jusqu_a_son_renouvellement()
    {
        var calls = 0;
        var factory = new StubHttpClientFactory(new StubHandler(request =>
        {
            calls++;
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"jeton-{{calls}}","expires_in":300}""", Encoding.UTF8, "application/json")
            };
        }));

        var provider = new ServiceTokenProvider(
            factory,
            Options.Create(new KeycloakOptions { Authority = "http://keycloak.test/realms/maison", Audience = "catalogue-api" }),
            Options.Create(new MetadataOptions { ClientSecret = "secret-de-test" }),
            TimeProvider.System);

        Assert.Equal("jeton-1", await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal("jeton-1", await provider.GetTokenAsync(CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Sans_secret_configure_le_jeton_n_est_pas_demande()
    {
        var provider = new ServiceTokenProvider(
            new StubHttpClientFactory(new StubHandler(_ => throw new InvalidOperationException("Appel inattendu"))),
            Options.Create(new KeycloakOptions { Authority = "http://keycloak.test/realms/maison", Audience = "catalogue-api" }),
            Options.Create(new MetadataOptions()),
            TimeProvider.System);

        await Assert.ThrowsAsync<ServiceTokenException>(() => provider.GetTokenAsync(CancellationToken.None));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
