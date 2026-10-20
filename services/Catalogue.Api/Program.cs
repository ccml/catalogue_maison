using Catalogue.Api.Features.Films;
using Catalogue.Api.Features.Loans;
using Catalogue.Api.Features.Medias;
using Catalogue.Api.Features.Provisioning;
using Catalogue.Api.Features.Scans;
using Catalogue.Api.Features.Sharing;
using Catalogue.Api.Features.Transfer;
using Catalogue.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCatalogueApi(builder.Configuration);

var app = builder.Build();

app.UseCatalogueApi();
app.MapMediaEndpoints();
app.MapLoanEndpoints();
app.MapTransferEndpoints();
app.MapScanEndpoints();
app.MapFilmEndpoints();
app.MapProvisioningEndpoints();
app.MapSharingEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

/// <summary>
/// Point d'entrée exposé pour <c>WebApplicationFactory</c> dans les tests
/// d'intégration.
/// </summary>
public partial class Program;
