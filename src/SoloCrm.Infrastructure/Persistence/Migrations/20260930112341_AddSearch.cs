using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace SoloCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "organizations",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(name, '')), 'A') || setweight(to_tsvector('simple', coalesce(regexp_replace(website, '[@./:?#=&_+-]+', ' ', 'g'), '')), 'B') || setweight(to_tsvector('simple', coalesce(city, '')), 'B')",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "opportunities",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(title, '')), 'A')",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "search_name",
                table: "contacts",
                type: "text",
                nullable: true,
                computedColumnSql: "btrim(coalesce(first_name, '') || ' ' || coalesce(last_name, ''))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "contacts",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('simple', coalesce(first_name, '')), 'A') || setweight(to_tsvector('simple', coalesce(last_name, '')), 'A') || setweight(to_tsvector('simple', coalesce(regexp_replace(email::text, '[@./:?#=&_+-]+', ' ', 'g'), '')), 'B')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_organizations_name_trgm",
                table: "organizations",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_organizations_search_vector",
                table: "organizations",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_search_vector",
                table: "opportunities",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_title_trgm",
                table: "opportunities",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_search_name_trgm",
                table: "contacts",
                column: "search_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_search_vector",
                table: "contacts",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_organizations_name_trgm",
                table: "organizations");

            migrationBuilder.DropIndex(
                name: "ix_organizations_search_vector",
                table: "organizations");

            migrationBuilder.DropIndex(
                name: "ix_opportunities_search_vector",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "ix_opportunities_title_trgm",
                table: "opportunities");

            migrationBuilder.DropIndex(
                name: "ix_contacts_search_name_trgm",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_search_vector",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "search_name",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "contacts");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
