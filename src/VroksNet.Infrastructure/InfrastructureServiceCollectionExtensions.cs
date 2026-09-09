using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Infrastructure.Connections;
using VroksNet.Infrastructure.Persistence;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Specifications;
using VroksNet.Infrastructure.Templating;

namespace VroksNet.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder(
            configuration.GetConnectionString("VroksNetDb") ?? "Data Source=vroksnet.db")
        {
            // busy_timeout (seconds) — see docs/project-brief.md section 3 on the concurrent-write risk.
            DefaultTimeout = 5
        };

        services.AddDbContextFactory<VroksNetDbContext>(options =>
            options.UseSqlite(connectionStringBuilder.ConnectionString));

        // Single-writer pattern: everything that mutates the database goes through this queue
        // instead of writing directly — see IDbWriteQueue.
        services.AddSingleton<DbWriteQueue>();
        services.AddSingleton<IDbWriteQueue>(sp => sp.GetRequiredService<DbWriteQueue>());
        services.AddHostedService<DbWriteBackgroundService>();

        services.AddScoped<IApiSpecificationRepository, ApiSpecificationRepository>();
        services.AddScoped<IConnectionRepository, ConnectionRepository>();
        services.AddScoped<ITestScenarioRepository, TestScenarioRepository>();
        services.AddScoped<ICallRecordRepository, CallRecordRepository>();
        services.AddScoped<ISpecificationParser, OpenApiSpecificationParser>();
        services.AddScoped<IAsyncApiSpecificationParser, AsyncApiSpecificationParser>();
        services.AddScoped<IResponseTemplateEngine, PassthroughResponseTemplateEngine>();
        // Stateless — no scoped dependencies of its own, so Singleton avoids reallocating it per request.
        services.AddSingleton<ISchemaValidator, SchemaValidator>();

        // Bare factory registration — ConnectionTester/MessageSender each request their own named
        // client via IHttpClientFactory.CreateClient(...) rather than a typed AddHttpClient<T>
        // registration.
        services.AddHttpClient();
        services.AddScoped<IConnectionTester, ConnectionTester>();
        services.AddScoped<IMessageSender, MessageSender>();

        return services;
    }

    /// <summary>Applies pending migrations and enables WAL journal mode. Call once at startup.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var contextFactory = services.GetRequiredService<IDbContextFactory<VroksNetDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
        // WAL mode isn't a connection-string option — it must be set per-connection via PRAGMA.
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
    }
}
