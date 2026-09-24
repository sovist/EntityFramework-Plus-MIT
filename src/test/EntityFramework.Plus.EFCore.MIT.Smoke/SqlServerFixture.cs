using Microsoft.EntityFrameworkCore;

namespace EntityFramework.Plus.EFCore.MIT.Smoke;

/// <summary>
/// Creates and seeds the smoke database once per test class. The server defaults to a local trusted
/// SQL Server; set <c>EFPLUS_MIT_SMOKE_CONNECTION</c> to point elsewhere.
/// </summary>
public sealed class SqlServerFixture : IDisposable
{
    public const string ConnectionVariable = "EFPLUS_MIT_SMOKE_CONNECTION";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable(ConnectionVariable)
        ?? "Server=localhost;Database=EFPlusMitSmoke;Trusted_Connection=True;TrustServerCertificate=True";

    public SqlServerFixture()
    {
        using var context = CreateContext();
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();
        SmokeDbContext.Seed(context);
    }

    /// <param name="retryOnFailure">
    /// Enables the retrying execution strategy, which makes EF Core buffer readers (<c>_readerColumns</c> is
    /// populated) — the mode in which a scalar future compiled as a scalar would swallow the following result sets.
    /// </param>
    public SmokeDbContext CreateContext(bool retryOnFailure = false, int tenantId = 1)
    {
        var options = new DbContextOptionsBuilder<SmokeDbContext>()
            .UseSqlServer(ConnectionString, sqlServer =>
            {
                if (retryOnFailure)
                {
                    sqlServer.EnableRetryOnFailure();
                }
            })
            .Options;

        return new SmokeDbContext(options) { TenantId = tenantId };
    }

    public void Dispose()
    {
        using var context = CreateContext();
        context.Database.EnsureDeleted();
    }
}