using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoloCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactOrganizationAndSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "contacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_organization_id",
                table: "contacts",
                column: "organization_id");

            migrationBuilder.AddForeignKey(
                name: "fk_contacts_organizations_organization_id",
                table: "contacts",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_contacts_organizations_organization_id",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_contacts_organization_id",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "source",
                table: "contacts");
        }
    }
}
