using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCallHistoryIndexesAndRequestLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "Timestamp",
                table: "CallRecords",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            // Rows written before this migration hold EF's DateTimeOffset text
            // ("2026-10-01 12:00:00.123+00:00"); convert them to UTC ticks. julianday()
            // normalizes the offset to UTC, and 1721425.5 is the Julian day of 0001-01-01T00:00Z
            // (tick 0). Double precision keeps this within ~50µs — plenty for ordering history.
            //
            // EF runs this UPDATE *before* the table rebuild the AlterColumn above needs (it
            // warns about that — event 30200 — which is expected here), so the integer lands in
            // the still-TEXT column as numeric text and the rebuild's INSERT…SELECT into the new
            // INTEGER column turns it back into an integer. The LIKE guard skips anything that's
            // already numeric, so this is also correct if EF ever runs it after the rebuild.
            migrationBuilder.Sql(
                """
                UPDATE "CallRecords"
                SET "Timestamp" = CAST(ROUND((julianday("Timestamp") - 1721425.5) * 864000000000.0) AS INTEGER)
                WHERE "Timestamp" LIKE '%-%';
                """);

            migrationBuilder.AddColumn<string>(
                name: "RequestLine",
                table: "CallRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_SpecificationId_Timestamp_Id",
                table: "CallRecords",
                columns: new[] { "SpecificationId", "Timestamp", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_Timestamp_Id",
                table: "CallRecords",
                columns: new[] { "Timestamp", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CallRecords_SpecificationId_Timestamp_Id",
                table: "CallRecords");

            migrationBuilder.DropIndex(
                name: "IX_CallRecords_Timestamp_Id",
                table: "CallRecords");

            migrationBuilder.DropColumn(
                name: "RequestLine",
                table: "CallRecords");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "Timestamp",
                table: "CallRecords",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            // Reverse of the Up conversion (to millisecond precision): UTC ticks back to EF's
            // DateTimeOffset text. Works whether EF runs it before or after the rebuild, for the
            // same reason as in Up.
            migrationBuilder.Sql(
                """
                UPDATE "CallRecords"
                SET "Timestamp" = strftime('%Y-%m-%d %H:%M:%f', CAST("Timestamp" AS INTEGER) / 864000000000.0 + 1721425.5) || '+00:00'
                WHERE "Timestamp" NOT LIKE '%-%';
                """);
        }
    }
}
