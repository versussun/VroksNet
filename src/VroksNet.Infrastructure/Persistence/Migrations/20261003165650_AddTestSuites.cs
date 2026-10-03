using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTestSuites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SuiteRunId",
                table: "TestRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SuiteRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TestSuiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledFor = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    FinishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuiteRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestSuites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TestScenarioIds = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestSuites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_SuiteRunId",
                table: "TestRuns",
                column: "SuiteRunId");

            migrationBuilder.CreateIndex(
                name: "IX_SuiteRuns_Status_ScheduledFor",
                table: "SuiteRuns",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_SuiteRuns_TestSuiteId_ScheduledFor",
                table: "SuiteRuns",
                columns: new[] { "TestSuiteId", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_TestSuites_Name",
                table: "TestSuites",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SuiteRuns");

            migrationBuilder.DropTable(
                name: "TestSuites");

            migrationBuilder.DropIndex(
                name: "IX_TestRuns_SuiteRunId",
                table: "TestRuns");

            migrationBuilder.DropColumn(
                name: "SuiteRunId",
                table: "TestRuns");
        }
    }
}
