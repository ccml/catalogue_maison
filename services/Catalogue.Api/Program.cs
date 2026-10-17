using Catalogue.Api.Features.Films;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Features.Scans;
using Catalogue.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCatalogueApi(builder.Configuration);

var app = builder.Build();

app.UseCatalogueApi();
app.MapMediaEndpoints();
app.MapScanEndpoints();
app.MapFilmEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

/// <summary>
/// Point d'entrée exposé pour <c>WebApplicationFactory</c> dans les tests
/// d'intégration.
/// </summary>
public partial class Program;
