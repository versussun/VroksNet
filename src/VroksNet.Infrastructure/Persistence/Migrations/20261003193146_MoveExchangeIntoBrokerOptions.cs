using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VroksNet.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR 0003: a Test Scenario's or Publisher's RabbitMQ <c>Exchange</c> becomes the <c>exchange</c>
    /// entry of its <c>BrokerOptions</c> JSON object. Not a rename: the value has to be wrapped
    /// (<c>orders</c> → <c>{"exchange":"orders"}</c>), and a row without an exchange has no options
    /// (NULL). Down unwraps it again, dropping any other option.
    /// </summary>
    public partial class MoveExchangeIntoBrokerOptions : Migration
    {
        private static readonly string[] Tables = ["TestScenarios", "Publishers"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.AddColumn<string>(
                    name: "BrokerOptions",
                    table: table,
                    type: "TEXT",
                    nullable: true);

                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"BrokerOptions\" = json_object('exchange', trim(\"Exchange\")) " +
                    "WHERE \"Exchange\" IS NOT NULL AND trim(\"Exchange\") <> '';");

                migrationBuilder.DropColumn(
                    name: "Exchange",
                    table: table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.AddColumn<string>(
                    name: "Exchange",
                    table: table,
                    type: "TEXT",
                    nullable: true);

                migrationBuilder.Sql(
                    $"UPDATE \"{table}\" SET \"Exchange\" = json_extract(\"BrokerOptions\", '$.exchange') " +
                    "WHERE \"BrokerOptions\" IS NOT NULL;");

                migrationBuilder.DropColumn(
                    name: "BrokerOptions",
                    table: table);
            }
        }
    }
}
