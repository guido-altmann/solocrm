using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoloCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "enrichment_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrichment_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "enrichment_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    proposed_value = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    current_value = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_urls = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrichment_suggestions", x => x.id);
                    table.CheckConstraint("ck_enrichment_suggestions_confidence", "confidence BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "fk_enrichment_suggestions_enrichment_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "enrichment_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_enrichment_requests_created_at",
                table: "enrichment_requests",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_enrichment_requests_requested_at",
                table: "enrichment_requests",
                column: "requested_at",
                filter: "status IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "ix_enrichment_requests_target_type_target_id",
                table: "enrichment_requests",
                columns: new[] { "target_type", "target_id" },
                unique: true,
                filter: "status IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "ix_enrichment_suggestions_request_id",
                table: "enrichment_suggestions",
                column: "request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrichment_suggestions");

            migrationBuilder.DropTable(
                name: "enrichment_requests");
        }
    }
}
