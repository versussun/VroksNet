using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using VroksNet.Application.Abstractions;
using VroksNet.Infrastructure.Brokers;
using VroksNet.Infrastructure.Brokers.Http;
using VroksNet.Infrastructure.Brokers.Kafka;
using VroksNet.Infrastructure.Brokers.Nats;
using VroksNet.Infrastructure.Brokers.RabbitMq;
using VroksNet.Infrastructure.Connections;
using VroksNet.Infrastructure.Hosting;
using VroksNet.Infrastructure.Persistence;
using VroksNet.Infrastructure.Publishing;
using VroksNet.Infrastructure.Scheduling;
using VroksNet.Infrastructure.SchemaValidation;
using VroksNet.Infrastructure.Specifications;
using VroksNet.Infrastructure.Templating;

namespace VroksNet.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var configuredConnectionString = configuration.GetConnectionString("VroksNetDb");

        // No ConnectionStrings:VroksNetDb configured → default to an in-memory SQLite database:
        // nothing to clean up locally, fresh state every process start. Set it explicitly — a
        // file path (ConnectionStrings__VroksNetDb env var, appsettings, user secrets, ...), or
        // the volume-backed path Docker sets (see Dockerfile) — to persist across restarts.
        var isInMemory = string.IsNullOrWhiteSpace(configuredConnectionString);

        var connectionStringBuilder = new SqliteConnectionStringBuilder(
            isInMemory
                // Named + shared cache, not the bare ":memory:" shorthand — ":memory:" gives every
                // new connection (and IDbContextFactory hands out a fresh one per DbContext) its
                // own private, empty database; shared cache is what lets them all see the same
                // data. See InMemoryDatabaseKeepAlive for why the database still needs a
                // dedicated connection held open for it to survive between those.
                ? "Data Source=VroksNetInMemoryDb;Mode=Memory;Cache=Shared"
                : configuredConnectionString!)
        {
            // busy_timeout (seconds) — see docs/project-brief.md section 3 on the concurrent-write risk.
            DefaultTimeout = 5
        };

        if (!isInMemory)
        {
            // SQLite creates the .db file itself if it's missing (default ReadWriteCreate mode)
            // but won't create missing parent directories — needed for a nested path like Docker's
            // "/app/data/vroksnet.db" on a fresh volume, or anything a user types into the
            // Settings page's "SQLite file path" field.
            var directory = Path.GetDirectoryName(connectionStringBuilder.DataSource);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        services.AddDbContextFactory<VroksNetDbContext>(options =>
            options.UseSqlite(connectionStringBuilder.ConnectionString));

        if (isInMemory)
        {
            services.AddSingleton(_ =>
            {
                var keepAliveConnection = new SqliteConnection(connectionStringBuilder.ConnectionString);
                keepAliveConnection.Open();
                return new InMemoryDatabaseKeepAlive(keepAliveConnection);
            });
        }

        // Surfaced read-only on the Settings page (GetStorageStatusHandler) — DataSource is the
        // file path in file mode, ignored in memory mode.
        services.AddSingleton<IStorageStatusProvider>(
            new StorageStatusProvider(isInMemory, isInMemory ? null : connectionStringBuilder.DataSource));

        // Single-writer pattern: everything that mutates the database goes through this queue
        // instead of writing directly — see IDbWriteQueue.
        services.AddSingleton<DbWriteQueue>();
        services.AddSingleton<IDbWriteQueue>(sp => sp.GetRequiredService<DbWriteQueue>());
        services.AddHostedService<DbWriteBackgroundService>();

        // Part of /health (see ServiceDefaults' MapDefaultEndpoints) — not of /alive: an unreachable
        // database makes the app not ready, but restarting it wouldn't fix a full or read-only disk.
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database")
            // Not ready until provisioning has run (ADR 0001); healthy when there's nothing to provision.
            .AddCheck<Provisioning.ProvisioningHealthCheck>("provisioning");

        services.AddSingleton<IProvisioningSource, Provisioning.FileProvisioningSource>();
        services.AddScoped<IProvisionedMarker, Provisioning.ProvisionedMarker>();
        services.AddSingleton<IProvisioningPackageWriter, Provisioning.ProvisioningPackageWriter>();
        // Right after the write queue's consumer, so provisioning's writes are drained.
        services.AddHostedService<Provisioning.ProvisioningHostedService>();

        services.AddScoped<IApiSpecificationRepository, ApiSpecificationRepository>();
        services.AddScoped<IConnectionRepository, ConnectionRepository>();
        services.AddScoped<ITestScenarioRepository, TestScenarioRepository>();
        services.AddScoped<IPublisherRepository, PublisherRepository>();
        services.AddScoped<ITestRunRepository, TestRunRepository>();
        services.AddScoped<ITestSuiteRepository, TestSuiteRepository>();
        services.AddScoped<ISuiteRunRepository, SuiteRunRepository>();
        services.AddScoped<ICallRecordRepository, CallRecordRepository>();
        services.AddScoped<ICallRecordNameResolver, CallRecordNameResolver>();
        services.AddSingleton<IProviderSettings>(new ProviderSettings(configuration));
        services.AddSingleton<IAppVersionProvider, AppVersionProvider>();
        services.AddSingleton<ICronSchedule, CronSchedule>();
        services.AddScoped<ISpecificationParser, OpenApiSpecificationParser>();
        services.AddScoped<IAsyncApiSpecificationParser, AsyncApiSpecificationParser>();
        // Stateless apart from the clock it reads for {{now}}.
        services.AddSingleton<IResponseTemplateEngine>(new ResponseTemplateEngine(TimeProvider.System));
        // Stateless — no scoped dependencies of its own, so Singleton avoids reallocating it per request.
        services.AddSingleton<ISchemaValidator, SchemaValidator>();

        services.AddBrokerAdapters();
        services.AddScoped<IConnectionTester, ConnectionTester>();
        services.AddScoped<IMessageSender, MessageSender>();
        services.AddSingleton<IMessageListener, MessageListener>();

        // The async-mock worker: publishes enabled publishers on their schedule.
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<PublisherBackgroundService>();
        // Registered after DbWriteBackgroundService, so it stops first on shutdown: its runs still
        // record themselves as Interrupted through the write queue.
        services.AddHostedService<TestRuns.TestRunBackgroundService>();

        return services;
    }

    /// <summary>
    /// One <see cref="IBrokerAdapter"/> per <see cref="Domain.Connections.ConnectionServiceType"/>
    /// (ADR 0003) and the registry the dispatchers look them up in. The adapters are stateless and
    /// open their own short-lived clients per call. Http requests its named client through the
    /// bare <see cref="IHttpClientFactory"/> registration rather than a typed AddHttpClient&lt;T&gt;.
    /// </summary>
    public static IServiceCollection AddBrokerAdapters(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<IBrokerAdapter, HttpBrokerAdapter>();
        services.AddSingleton<IBrokerAdapter, RabbitMqBrokerAdapter>();
        services.AddSingleton<IBrokerAdapter, NatsBrokerAdapter>();
        services.AddSingleton<IBrokerAdapter, KafkaBrokerAdapter>();
        services.AddSingleton<BrokerAdapterRegistry>();
        return services;
    }

    /// <summary>Applies pending migrations and enables WAL journal mode. Call once at startup.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // Must happen first: in in-memory mode this opens the connection that keeps the
        // shared-cache database alive between the short-lived ones IDbContextFactory hands out
        // below — otherwise the database created by MigrateAsync would vanish the moment its
        // connection closes. GetService (not GetRequiredService) — null and a no-op against a
        // file-backed database, where nothing is registered.
        services.GetService<InMemoryDatabaseKeepAlive>();

        var contextFactory = services.GetRequiredService<IDbContextFactory<VroksNetDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
        // WAL mode isn't a connection-string option — it must be set per-connection via PRAGMA.
        // (A no-op against an in-memory database — SQLite doesn't support WAL there — but
        // harmless to still call unconditionally.)
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
    }
}
