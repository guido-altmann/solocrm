using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SoloCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStagesAndOpportunities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "opportunities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stage_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agency_organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primary_contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    utilization = table.Column<int>(type: "integer", nullable: true),
                    remote_percentage = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    lost_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    extra_fields = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    duration_unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    duration_value = table.Column<int>(type: "integer", nullable: true),
                    pricing_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    pricing_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    pricing_model = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opportunities", x => x.id);
                    table.CheckConstraint("ck_opportunities_duration_value", "duration_value > 0");
                    table.CheckConstraint("ck_opportunities_pricing_amount", "pricing_amount > 0");
                    table.CheckConstraint("ck_opportunities_remote_percentage", "remote_percentage BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_opportunities_utilization", "utilization BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_opportunities_contacts_primary_contact_id",
                        column: x => x.primary_contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_opportunities_organizations_agency_organization_id",
                        column: x => x.agency_organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_opportunities_organizations_client_organization_id",
                        column: x => x.client_organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_opportunities_stages_stage_id",
                        column: x => x.stage_id,
                        principalTable: "stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "stages",
                columns: new[] { "id", "created_at", "name", "sort_order", "status", "updated_at" },
                values: new object[,]
                {
                    { new Guid("0199a000-0000-7000-8000-000000000001"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Neu", 1, "Open", new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("0199a000-0000-7000-8000-000000000002"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Beworben", 2, "Open", new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("0199a000-0000-7000-8000-000000000003"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Im Gespräch", 3, "Open", new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("0199a000-0000-7000-8000-000000000004"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Angebot", 4, "Open", new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("0199a000-0000-7000-8000-000000000005"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Gewonnen", 5, "Won", new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("0199a000-0000-7000-8000-000000000006"), new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Verloren", 6, "Lost", new DateTimeOffset(new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_agency_organization_id",
                table: "opportunities",
                column: "agency_organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_client_organization_id",
                table: "opportunities",
                column: "client_organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_primary_contact_id",
                table: "opportunities",
                column: "primary_contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_stage_id",
                table: "opportunities",
                column: "stage_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opportunities");

            migrationBuilder.DropTable(
                name: "stages");
        }
    }
}
