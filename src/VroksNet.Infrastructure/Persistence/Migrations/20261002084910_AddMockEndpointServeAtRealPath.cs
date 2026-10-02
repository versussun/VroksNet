using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMockEndpointServeAtRealPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ServeAtRealPath",
                table: "MockEndpoints",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServeAtRealPath",
                table: "MockEndpoints");
        }
    }
}
