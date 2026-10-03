using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisionedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProvisionedAt",
                table: "TestScenarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProvisionedAt",
                table: "Publishers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProvisionedAt",
                table: "Connections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProvisionedAt",
                table: "ApiSpecifications",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProvisionedAt",
                table: "TestScenarios");

            migrationBuilder.DropColumn(
                name: "ProvisionedAt",
                table: "Publishers");

            migrationBuilder.DropColumn(
                name: "ProvisionedAt",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "ProvisionedAt",
                table: "ApiSpecifications");
        }
    }
}
