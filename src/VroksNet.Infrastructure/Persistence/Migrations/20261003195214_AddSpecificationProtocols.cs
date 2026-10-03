using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// R5 of docs/broker-adapters-plan.md: an AsyncAPI spec's server protocols, as a JSON list.
    /// Existing rows get "[]" — never "", which wouldn't deserialize — and fill in on their next import.
    /// </summary>
    public partial class AddSpecificationProtocols : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Protocols",
                table: "ApiSpecifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Protocols",
                table: "ApiSpecifications");
        }
    }
}
