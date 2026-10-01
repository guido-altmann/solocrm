using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoloCrm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_country_code",
                table: "organizations",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_postal_code",
                table: "organizations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_region",
                table: "organizations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_street",
                table: "organizations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_street2",
                table: "organizations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_city",
                table: "contacts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_country_code",
                table: "contacts",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_postal_code",
                table: "contacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_region",
                table: "contacts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_street",
                table: "contacts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_street2",
                table: "contacts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address_country_code",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "address_postal_code",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "address_region",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "address_street",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "address_street2",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "address_city",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "address_country_code",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "address_postal_code",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "address_region",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "address_street",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "address_street2",
                table: "contacts");
        }
    }
}
