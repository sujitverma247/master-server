using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MasterServer.Data;

#nullable disable

namespace MasterServer.Data.Migrations
{
    [DbContext(typeof(AppDb))]
    [Migration("20261016054600_TicketWebsiteFormAddress")]
    public partial class TicketWebsiteFormAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "Tickets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Tickets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "Tickets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Tickets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Tickets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Street", table: "Tickets");
            migrationBuilder.DropColumn(name: "City", table: "Tickets");
            migrationBuilder.DropColumn(name: "Region", table: "Tickets");
            migrationBuilder.DropColumn(name: "PostalCode", table: "Tickets");
            migrationBuilder.DropColumn(name: "Country", table: "Tickets");
        }
    }
}
