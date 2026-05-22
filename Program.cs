using MultiAgentSimWeb.Components;
using MultiAgentSimWeb.Services;
using MultiAgentSimWeb.Services.Maps;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Simulation wiring — swap any of these to change behavior without touching the loop.
builder.Services.AddSingleton<IMapGenerator, DefaultMapGenerator>();
builder.Services.AddSingleton<AgentProfileService>();
// Scoped (per-circuit) so each browser session gets its own diagnostics — matches
// SimulationService's lifetime and avoids cross-session CSV/total corruption.
builder.Services.AddScoped<LlmDiagnosticsService>();
builder.Services.AddScoped<SimulationService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
