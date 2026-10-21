using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sraz.Infrastructure.Persistence;

public sealed class SrazDbContextFactory : IDesignTimeDbContextFactory<SrazDbContext>
{
    public SrazDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? throw new InvalidOperationException("ConnectionStrings__Default is required.");
        var options = new DbContextOptionsBuilder<SrazDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new SrazDbContext(options);
    }
}
