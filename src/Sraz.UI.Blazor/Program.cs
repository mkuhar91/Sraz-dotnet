using Microsoft.EntityFrameworkCore;
using Sraz.Application.Abstractions;
using Sraz.Infrastructure.Persistence;

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

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<SrazDbContext>();
    await dbContext.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<QuestionSeedImporter>().ImportAsync();
}

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
