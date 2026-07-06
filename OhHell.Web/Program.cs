using OhHell.Components.Services;
using OhHell.Web;
using OhHell.Web.Hubs;
using OhHell.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR();
builder.Services.AddScoped<GameSession>();
builder.Services.AddSingleton<MultiplayerGameService>();
builder.Services.AddScoped<IPlatformStorage, WebStorage>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(OhHell.Components.App).Assembly);
app.MapHub<GameLobbyHub>("/hubs/lobby");

app.Run();
