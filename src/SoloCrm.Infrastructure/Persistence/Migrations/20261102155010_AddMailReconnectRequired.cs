using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoloCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMailReconnectRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_reconnect_required",
                table: "mail_connections",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_reconnect_required",
                table: "mail_connections");
        }
    }
}
