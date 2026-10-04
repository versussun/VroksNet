using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMockEndpointRequestExample : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequestExampleIsGenerated",
                table: "MockEndpoints",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RequestExampleTemplate",
                table: "MockEndpoints",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestExampleIsGenerated",
                table: "MockEndpoints");

            migrationBuilder.DropColumn(
                name: "RequestExampleTemplate",
                table: "MockEndpoints");
        }
    }
}
