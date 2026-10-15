using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MasterServer.Data;

#nullable disable

namespace MasterServer.Data.Migrations
{
    [DbContext(typeof(AppDb))]
    [Migration("20261016000000_CustomerMailLocale")]
    public partial class CustomerMailLocale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "Customers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "MailOutbox",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Locale",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "MailOutbox");
        }
    }
}
