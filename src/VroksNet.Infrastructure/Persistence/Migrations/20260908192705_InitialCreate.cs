using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiSpecifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    RawContent = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiSpecifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CallRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SpecificationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MockEndpointId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RequestSnapshot = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseSnapshot = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MockEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SpecificationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationKey = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExampleTemplate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MockEndpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MockEndpoints_ApiSpecifications_SpecificationId",
                        column: x => x.SpecificationId,
                        principalTable: "ApiSpecifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiSpecifications_Title",
                table: "ApiSpecifications",
                column: "Title",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MockEndpoints_SpecificationId",
                table: "MockEndpoints",
                column: "SpecificationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallRecords");

            migrationBuilder.DropTable(
                name: "MockEndpoints");

            migrationBuilder.DropTable(
                name: "ApiSpecifications");
        }
    }
}
