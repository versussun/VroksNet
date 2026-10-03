using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Names become unique per kind. Existing data may break that, so before the indexes:
            // names are trimmed (they're stored trimmed from now on), duplicates are renamed
            // "Name (2)", "Name (3)", ... in creation order, and any rename that still collides with
            // an existing name gets the start of its id appended. Ranks are computed into a temp
            // table first: an UPDATE's own subqueries would see rows it already renamed.
            foreach (var table in new[] { "TestScenarios", "Publishers" })
            {
                migrationBuilder.Sql($"UPDATE \"{table}\" SET \"Name\" = TRIM(\"Name\") WHERE \"Name\" <> TRIM(\"Name\");");
                RenameDuplicates(migrationBuilder, table, "' (' || \"Rank\" || ')'");
                RenameDuplicates(migrationBuilder, table, "' [' || substr(\"Id\", 1, 8) || ']'");
            }

            migrationBuilder.CreateIndex(
                name: "IX_TestScenarios_Name",
                table: "TestScenarios",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Publishers_Name",
                table: "Publishers",
                column: "Name",
                unique: true);
        }

        /// <summary>Appends <paramref name="suffixSql"/> to every row of <paramref name="table"/> whose name an earlier row (by CreatedAt, then Id) already has.</summary>
        private static void RenameDuplicates(MigrationBuilder migrationBuilder, string table, string suffixSql)
        {
            migrationBuilder.Sql($"""
                CREATE TEMP TABLE "UniqueNameRanks" AS
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Name" ORDER BY "CreatedAt", "Id") AS "Rank"
                    FROM "{table}";
                UPDATE "{table}"
                    SET "Name" = "Name" || (SELECT {suffixSql} FROM "UniqueNameRanks" WHERE "UniqueNameRanks"."Id" = "{table}"."Id")
                    WHERE "Id" IN (SELECT "Id" FROM "UniqueNameRanks" WHERE "Rank" > 1);
                DROP TABLE "UniqueNameRanks";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestScenarios_Name",
                table: "TestScenarios");

            migrationBuilder.DropIndex(
                name: "IX_Publishers_Name",
                table: "Publishers");
        }
    }
}
