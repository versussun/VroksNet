using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.Publishers;
using VroksNet.Domain.TestRuns;
using VroksNet.Domain.TestScenarios;
using VroksNet.Domain.TestSuites;

namespace VroksNet.Infrastructure.Persistence;

public sealed class VroksNetDbContext(DbContextOptions<VroksNetDbContext> options) : DbContext(options)
{
    public DbSet<ApiSpecification> ApiSpecifications => Set<ApiSpecification>();

    public DbSet<MockEndpoint> MockEndpoints => Set<MockEndpoint>();

    public DbSet<CallRecord> CallRecords => Set<CallRecord>();

    public DbSet<Connection> Connections => Set<Connection>();

    public DbSet<TestScenario> TestScenarios => Set<TestScenario>();

    public DbSet<Publisher> Publishers => Set<Publisher>();

    public DbSet<TestRun> TestRuns => Set<TestRun>();

    public DbSet<TestSuite> TestSuites => Set<TestSuite>();

    public DbSet<SuiteRun> SuiteRuns => Set<SuiteRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiSpecification>(entity =>
        {
            entity.HasKey(specification => specification.Id);
            // The version-matching key from docs/project-brief.md section 2 — a re-uploaded
            // spec replaces the existing row with the same title, so it must stay unique.
            entity.HasIndex(specification => specification.Title).IsUnique();
            entity.HasMany(specification => specification.Endpoints)
                .WithOne()
                .HasForeignKey(endpoint => endpoint.SpecificationId)
                .OnDelete(DeleteBehavior.Cascade);
            // Its AsyncAPI servers' protocols — a short list only ever read and written with its spec.
            entity.Property(specification => specification.Protocols)
                .HasConversion(
                    protocols => JsonSerializer.Serialize(protocols, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>(),
                    new ValueComparer<List<string>>(
                        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
                        protocols => protocols.Aggregate(0, (hash, protocol) => HashCode.Combine(hash, protocol)),
                        protocols => protocols.ToList()));
        });

        modelBuilder.Entity<MockEndpoint>(entity =>
        {
            entity.HasKey(endpoint => endpoint.Id);
            // Stored as one JSON object column — it's only ever read/written whole, together with
            // its endpoint, so a child table would add joins for nothing.
            entity.Property(endpoint => endpoint.ResponseSchemasByStatus)
                .HasConversion(
                    schemas => JsonSerializer.Serialize(schemas, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<Dictionary<string, string?>>(json, (JsonSerializerOptions?)null) ?? new Dictionary<string, string?>(),
                    new ValueComparer<Dictionary<string, string?>>(
                        (left, right) => left == null ? right == null : right != null && left.Count == right.Count && !left.Except(right).Any(),
                        // XOR, so the hash doesn't depend on enumeration order — equal dictionaries filled in
                        // a different order must hash the same.
                        schemas => schemas.Aggregate(0, (hash, pair) => hash ^ HashCode.Combine(pair.Key, pair.Value)),
                        schemas => new Dictionary<string, string?>(schemas)));
        });

        modelBuilder.Entity<CallRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            // Stored as UTC ticks (INTEGER), not EF's default ISO text: SQLite can't ORDER BY or
            // compare DateTimeOffset text, and text order would be wrong across offsets anyway.
            // The offset itself isn't kept — every record is written with DateTimeOffset.UtcNow.
            entity.Property(record => record.Timestamp)
                .HasConversion(timestamp => timestamp.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
            // The call history is always read newest first by (Timestamp, Id) — the keyset — and
            // optionally narrowed to one spec; Id in the index lets it satisfy the whole sort.
            entity.HasIndex(record => new { record.Timestamp, record.Id });
            entity.HasIndex(record => new { record.SpecificationId, record.Timestamp, record.Id });
        });

        modelBuilder.Entity<Connection>(entity =>
        {
            entity.HasKey(connection => connection.Id);
            entity.HasIndex(connection => connection.Name).IsUnique();
        });

        // Names are unique per kind (ADR 0001: provisioning refers to objects by name), like
        // Connection.Name above. Migration AddUniqueNames renamed any duplicates before adding these.
        modelBuilder.Entity<TestScenario>(entity =>
        {
            entity.HasKey(scenario => scenario.Id);
            entity.HasIndex(scenario => scenario.Name).IsUnique();
            entity.Property(scenario => scenario.BrokerOptions).HasConversion<BrokerOptionsConverter>();
        });

        // Read whole by the worker every second and filtered in memory (PublisherSchedule.IsDue) —
        // there are only ever a handful, so LastPublishedAt never needs ordering in SQL.
        modelBuilder.Entity<Publisher>(entity =>
        {
            entity.HasKey(publisher => publisher.Id);
            entity.HasIndex(publisher => publisher.Name).IsUnique();
            entity.Property(publisher => publisher.BrokerOptions).HasConversion<BrokerOptionsConverter>();
        });

        modelBuilder.Entity<TestRun>(entity =>
        {
            entity.HasKey(run => run.Id);
            // UTC ticks, like CallRecord.Timestamp: the history is ordered and the worker filters
            // "due" runs by ScheduledFor in SQL, which SQLite can't do on DateTimeOffset text.
            entity.Property(run => run.ScheduledFor)
                .HasConversion(time => time.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
            entity.Property(run => run.StartedAt)
                .HasConversion(time => time.HasValue ? time.Value.UtcTicks : (long?)null, ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null);
            entity.Property(run => run.FinishedAt)
                .HasConversion(time => time.HasValue ? time.Value.UtcTicks : (long?)null, ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null);
            // A scenario's history, newest first (the keyset includes Id), and the worker's
            // "queued and due" lookup every second.
            entity.HasIndex(run => new { run.TestScenarioId, run.ScheduledFor, run.Id });
            entity.HasIndex(run => new { run.Status, run.ScheduledFor });
            // A suite run's own runs.
            entity.HasIndex(run => run.SuiteRunId);
        });

        modelBuilder.Entity<TestSuite>(entity =>
        {
            entity.HasKey(suite => suite.Id);
            // Unique: CI addresses a suite by name.
            entity.HasIndex(suite => suite.Name).IsUnique();
            // The scenario ids in order, as one JSON column — a suite is read and written whole.
            entity.Property(suite => suite.TestScenarioIds)
                .HasConversion(
                    ids => JsonSerializer.Serialize(ids, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<Guid>>(json, (JsonSerializerOptions?)null) ?? new List<Guid>(),
                    new ValueComparer<List<Guid>>(
                        (left, right) => left == null ? right == null : right != null && left.SequenceEqual(right),
                        ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
                        ids => ids.ToList()));
        });

        modelBuilder.Entity<SuiteRun>(entity =>
        {
            entity.HasKey(run => run.Id);
            // UTC ticks, like TestRun: ordered history and "due" selection in SQL.
            entity.Property(run => run.ScheduledFor)
                .HasConversion(time => time.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
            entity.Property(run => run.StartedAt)
                .HasConversion(time => time.HasValue ? time.Value.UtcTicks : (long?)null, ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null);
            entity.Property(run => run.FinishedAt)
                .HasConversion(time => time.HasValue ? time.Value.UtcTicks : (long?)null, ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null);
            entity.HasIndex(run => new { run.TestSuiteId, run.ScheduledFor });
            entity.HasIndex(run => new { run.Status, run.ScheduledFor });
        });
    }
}
