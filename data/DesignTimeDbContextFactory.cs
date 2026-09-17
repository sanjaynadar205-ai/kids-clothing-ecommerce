using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KidsWearStore.Data;

public class DesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder =
            new DbContextOptionsBuilder<ApplicationDbContext>();

        optionsBuilder.UseSqlServer(
            @"Server=(localdb)\KidsWearLocalDB;Database=KidsWearStoreDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True");

        return new ApplicationDbContext(
            optionsBuilder.Options);
    }
}

public class PostgreSqlApplicationDbContextFactory
    : IDesignTimeDbContextFactory<PostgreSqlApplicationDbContext>
{
    public PostgreSqlApplicationDbContext CreateDbContext(
        string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "NEON_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "NEON_CONNECTION_STRING environment variable is not configured.");
        }

        var optionsBuilder =
            new DbContextOptionsBuilder<
                PostgreSqlApplicationDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null);
            });

        return new PostgreSqlApplicationDbContext(
            optionsBuilder.Options);
    }
}

public class PostgreSqlApplicationDbContext
    : ApplicationDbContext
{
    public PostgreSqlApplicationDbContext(
        DbContextOptions<PostgreSqlApplicationDbContext> options)
        : base(options)
    {
    }
}