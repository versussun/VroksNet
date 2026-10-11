using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProtoSpecifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RpcKind",
                table: "MockEndpoints",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProtoDescriptorSet",
                table: "ApiSpecifications",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtoImports",
                table: "ApiSpecifications",
                type: "TEXT",
                nullable: false,
                // A JSON object, like the other JSON columns' defaults — "" wouldn't deserialize.
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "ProtoPath",
                table: "ApiSpecifications",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RpcKind",
                table: "MockEndpoints");

            migrationBuilder.DropColumn(
                name: "ProtoDescriptorSet",
                table: "ApiSpecifications");

            migrationBuilder.DropColumn(
                name: "ProtoImports",
                table: "ApiSpecifications");

            migrationBuilder.DropColumn(
                name: "ProtoPath",
                table: "ApiSpecifications");
        }
    }
}
