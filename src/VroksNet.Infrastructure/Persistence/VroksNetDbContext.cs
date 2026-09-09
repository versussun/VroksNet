using Microsoft.EntityFrameworkCore;
using VroksNet.Domain.ApiSpecifications;
using VroksNet.Domain.CallRecords;
using VroksNet.Domain.Connections;
using VroksNet.Domain.MockEndpoints;
using VroksNet.Domain.TestScenarios;

namespace VroksNet.Infrastructure.Persistence;

public sealed class VroksNetDbContext(DbContextOptions<VroksNetDbContext> options) : DbContext(options)
{
    public DbSet<ApiSpecification> ApiSpecifications => Set<ApiSpecification>();

    public DbSet<MockEndpoint> MockEndpoints => Set<MockEndpoint>();

    public DbSet<CallRecord> CallRecords => Set<CallRecord>();

    public DbSet<Connection> Connections => Set<Connection>();

    public DbSet<TestScenario> TestScenarios => Set<TestScenario>();

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

        modelBuilder.Entity<MockEndpoint>(entity => entity.HasKey(endpoint => endpoint.Id));

        modelBuilder.Entity<CallRecord>(entity => entity.HasKey(record => record.Id));

        modelBuilder.Entity<Connection>(entity =>
        {
            entity.HasKey(connection => connection.Id);
            entity.HasIndex(connection => connection.Name).IsUnique();
        });

        modelBuilder.Entity<TestScenario>(entity => entity.HasKey(scenario => scenario.Id));
    }
}
