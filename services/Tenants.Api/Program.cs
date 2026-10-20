using Tenants.Api.Features.Households;
using Tenants.Api.Features.Members;
using Tenants.Api.Features.Sharing;
using Tenants.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTenantsApi(builder.Configuration);

var app = builder.Build();

app.UseTenantsApi();
app.MapMemberEndpoints();
app.MapHouseholdEndpoints();
app.MapSharingEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

/// <summary>
/// Point d'entrée exposé pour <c>WebApplicationFactory</c> dans les tests
/// d'intégration.
/// </summary>
public partial class Program;
