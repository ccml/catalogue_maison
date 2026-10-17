using Metadata.Api.Features.Lookups;
using Metadata.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMetadataApi(builder.Configuration);

var app = builder.Build();

app.UseMetadataApi();
app.MapLookupEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

/// <summary>
/// Point d'entrée exposé pour <c>WebApplicationFactory</c> dans les tests
/// d'intégration.
/// </summary>
public partial class Program;
