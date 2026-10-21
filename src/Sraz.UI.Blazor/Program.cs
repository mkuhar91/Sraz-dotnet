using Microsoft.EntityFrameworkCore;
using Sraz.Application.Abstractions;
using Sraz.Application.Rooms;
using Sraz.Application.Services;
using Sraz.Infrastructure.Persistence;
using Sraz.UI.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is required.");

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<SrazDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IQuestionProvider, DbQuestionProvider>();
builder.Services.AddScoped<QuestionSeedImporter>(services =>
    new QuestionSeedImporter(
        services.GetRequiredService<SrazDbContext>(),
        new JsonQuestionProvider(Path.Combine(AppContext.BaseDirectory, "data", "questions.seed.json"))));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPlayerIdentity, CookiePlayerIdentity>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NicknameStore>();
builder.Services.AddSingleton<IRoomStore, EfRoomStore>();
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

await app.Services.GetRequiredService<IRoomManager>().LoadAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapHealthChecks("/health");
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

await app.RunAsync();
