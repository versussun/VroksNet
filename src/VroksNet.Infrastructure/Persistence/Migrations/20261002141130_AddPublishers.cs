using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublisherId",
                table: "CallRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Publishers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SpecificationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MockEndpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadOverride = table.Column<string>(type: "TEXT", nullable: true),
                    Exchange = table.Column<string>(type: "TEXT", nullable: true),
                    IntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastPublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastPublishSuccess = table.Column<bool>(type: "INTEGER", nullable: true),
                    LastPublishMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Publishers", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Publishers");

            migrationBuilder.DropColumn(
                name: "PublisherId",
                table: "CallRecords");
        }
    }
}
