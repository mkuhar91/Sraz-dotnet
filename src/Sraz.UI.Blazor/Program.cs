using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Sraz.Application.Abstractions;
using Sraz.Application.Rooms;
using Sraz.Application.Services;
using Sraz.Infrastructure.Persistence;
using Sraz.UI.Blazor.Services;
using Sraz.UI.Blazor.Localization;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is required.");

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddLocalization();
builder.Services.AddScoped<Texts>();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture("hr").AddSupportedCultures("hr", "en").AddSupportedUICultures("hr", "en");
    options.RequestCultureProviders = new List<IRequestCultureProvider> { new CookieRequestCultureProvider() };
});
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<SrazDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IQuestionProvider, DbQuestionProvider>();
builder.Services.AddScoped<QuestionSeedImporter>(services =>
    new QuestionSeedImporter(
        services.GetRequiredService<SrazDbContext>(),
        new JsonQuestionProvider(Path.Combine(AppContext.BaseDirectory, "data", "questions.seed.json"))));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPlayerIdentity, CookiePlayerIdentity>();
builder.Services.AddSingleton<PlayerConnectionTracker>();
builder.Services.AddHostedService(services => services.GetRequiredService<PlayerConnectionTracker>());
builder.Services.AddScoped<CircuitHandler, PlayerCircuitHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NicknameStore>();
builder.Services.AddSingleton<IRoomStore, EfRoomStore>();
builder.Services.AddSingleton<IRoomLeaderboard, EfRoomLeaderboard>();
builder.Services.AddSingleton<CachedQuestionProvider>();
builder.Services.AddSingleton<INpcStrategy>(_ => new RandomNpcStrategy());
builder.Services.AddSingleton<IRoomManager>(services => new RoomManager(
    services.GetRequiredService<IRoomStore>(),
    services.GetRequiredService<CachedQuestionProvider>(),
    services.GetRequiredService<INpcStrategy>(),
    services.GetRequiredService<TimeProvider>(),
    builder.Configuration.GetSection("Rooms").Get<RoomManagerOptions>()));
builder.Services.AddHostedService<RoomMaintenanceService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
    await dbContext.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<QuestionSeedImporter>().ImportAsync();
}

await app.Services.GetRequiredService<IRoomManager>().LoadAsync(disconnectPlayers: true);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRequestLocalization();
app.UseRouting();
app.MapGet("/culture", (HttpContext context, string culture, string? returnUrl) =>
{
    if (culture is not ("hr" or "en"))
    {
        return Results.BadRequest();
    }

    context.Response.Cookies.Append(
        CookieRequestCultureProvider.DefaultCookieName,
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
        new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Expires = app.Services.GetRequiredService<TimeProvider>().GetUtcNow().AddYears(1)
        });
    var destination = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
        ? returnUrl : "/";
    return Results.LocalRedirect(destination);
});
app.MapHealthChecks("/health");
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

await app.RunAsync();
