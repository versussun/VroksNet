using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResponseSchemasByStatusAndCallRecordContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResponseSchemasByStatus",
                table: "MockEndpoints",
                type: "TEXT",
                nullable: false,
                // An empty JSON object, not "" — existing rows must still deserialize (as "no
                // declared responses", i.e. nothing to validate until the spec is re-imported).
                defaultValue: "{}");

            migrationBuilder.AddColumn<bool>(
                name: "ContractValid",
                table: "CallRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusCode",
                table: "CallRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TestScenarioId",
                table: "CallRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationErrors",
                table: "CallRecords",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponseSchemasByStatus",
                table: "MockEndpoints");

            migrationBuilder.DropColumn(
                name: "ContractValid",
                table: "CallRecords");

            migrationBuilder.DropColumn(
                name: "StatusCode",
                table: "CallRecords");

            migrationBuilder.DropColumn(
                name: "TestScenarioId",
                table: "CallRecords");

            migrationBuilder.DropColumn(
                name: "ValidationErrors",
                table: "CallRecords");
        }
    }
}
