using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoloCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpportunityReceivedOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing requests came in on the day they were recorded, in the default time zone (iteration 6 decision 11).
            migrationBuilder.AddColumn<DateOnly>(
                name: "received_on",
                table: "opportunities",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("UPDATE opportunities SET received_on = (created_at AT TIME ZONE 'Europe/Berlin')::date;");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "received_on",
                table: "opportunities",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "received_on",
                table: "opportunities");
        }
    }
}
