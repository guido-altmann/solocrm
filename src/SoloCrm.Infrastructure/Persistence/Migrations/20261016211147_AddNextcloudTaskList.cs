using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoloCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNextcloudTaskList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "task_list_href",
                table: "nextcloud_connections",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "task_sync_token",
                table: "nextcloud_connections",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_nextcloud_sync_items_href",
                table: "nextcloud_sync_items",
                column: "href");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nextcloud_sync_items_href",
                table: "nextcloud_sync_items");

            migrationBuilder.DropColumn(
                name: "task_list_href",
                table: "nextcloud_connections");

            migrationBuilder.DropColumn(
                name: "task_sync_token",
                table: "nextcloud_connections");
        }
    }
}
