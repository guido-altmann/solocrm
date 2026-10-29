using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoloCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTagSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "sequence",
                table: "tags",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            // The column fills existing rows in arbitrary order; renumber them by creation and move the identity past them.
            migrationBuilder.Sql(
                """
                UPDATE tags SET sequence = numbered.n
                FROM (SELECT id, row_number() OVER (ORDER BY created_at, id) AS n FROM tags) AS numbered
                WHERE tags.id = numbered.id;
                SELECT setval(pg_get_serial_sequence('tags', 'sequence'), COALESCE((SELECT max(sequence) FROM tags), 0) + 1, false);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sequence",
                table: "tags");
        }
    }
}
