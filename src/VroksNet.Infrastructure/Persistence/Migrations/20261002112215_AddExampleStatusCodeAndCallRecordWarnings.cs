using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExampleStatusCodeAndCallRecordWarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExampleStatusCode",
                table: "MockEndpoints",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Warnings",
                table: "CallRecords",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExampleStatusCode",
                table: "MockEndpoints");

            migrationBuilder.DropColumn(
                name: "Warnings",
                table: "CallRecords");
        }
    }
}
