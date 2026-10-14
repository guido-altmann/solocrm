using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoloCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailLogging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "direction",
                table: "activities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "activities",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.CreateTable(
                name: "mail_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    internet_message_id = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: false),
                    folder = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_mail_messages_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mail_sync_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    folder = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delta_token = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_sync_states", x => x.id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_activities_direction_only_for_mail",
                table: "activities",
                sql: "(origin = 'Mail') = (direction IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_mail_messages_activity_id",
                table: "mail_messages",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_mail_messages_provider_internet_message_id",
                table: "mail_messages",
                columns: new[] { "provider", "internet_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mail_sync_states_provider_folder",
                table: "mail_sync_states",
                columns: new[] { "provider", "folder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mail_messages");

            migrationBuilder.DropTable(
                name: "mail_sync_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_activities_direction_only_for_mail",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "direction",
                table: "activities");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "activities");
        }
    }
}
