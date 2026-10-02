using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.Publishers;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Persistence;

public sealed class VroksNetDbContext(DbContextOptions<VroksNetDbContext> options) : DbContext(options)
{
    public DbSet<ApiSpecification> ApiSpecifications => Set<ApiSpecification>();

    public DbSet<MockEndpoint> MockEndpoints => Set<MockEndpoint>();

    public DbSet<CallRecord> CallRecords => Set<CallRecord>();

    public DbSet<Connection> Connections => Set<Connection>();

    public DbSet<TestScenario> TestScenarios => Set<TestScenario>();

    public DbSet<Publisher> Publishers => Set<Publisher>();

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

        modelBuilder.Entity<TestScenario>(entity => entity.HasKey(scenario => scenario.Id));

        // Read whole by the worker every second and filtered in memory (PublisherSchedule.IsDue) —
        // there are only ever a handful, so LastPublishedAt never needs ordering in SQL.
        modelBuilder.Entity<Publisher>(entity => entity.HasKey(publisher => publisher.Id));
    }
}
