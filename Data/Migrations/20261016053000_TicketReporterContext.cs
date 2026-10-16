using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MasterServer.Data;

#nullable disable

namespace MasterServer.Data.Migrations
{
    [DbContext(typeof(AppDb))]
    [Migration("20261016053000_TicketReporterContext")]
    public partial class TicketReporterContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BrandId",
                table: "Tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Tickets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Browser",
                table: "Tickets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Device",
                table: "Tickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Geo",
                table: "Tickets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "Tickets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Isp",
                table: "Tickets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Tickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OsVersion",
                table: "Tickets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Tickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Screen",
                table: "Tickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "Tickets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_BrandId",
                table: "Tickets",
                column: "BrandId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tickets_Brands_BrandId",
                table: "Tickets",
                column: "BrandId",
                principalTable: "Brands",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("""
                UPDATE "Tickets" AS t
                SET "BrandId" = p."BrandId"
                FROM "Products" AS p
                WHERE t."ProductId" = p."Id"
                  AND t."BrandId" IS NULL
                  AND p."BrandId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tickets_Brands_BrandId",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_BrandId",
                table: "Tickets");

            migrationBuilder.DropColumn(name: "Address", table: "Tickets");
            migrationBuilder.DropColumn(name: "BrandId", table: "Tickets");
            migrationBuilder.DropColumn(name: "Browser", table: "Tickets");
            migrationBuilder.DropColumn(name: "Device", table: "Tickets");
            migrationBuilder.DropColumn(name: "Geo", table: "Tickets");
            migrationBuilder.DropColumn(name: "IpAddress", table: "Tickets");
            migrationBuilder.DropColumn(name: "Isp", table: "Tickets");
            migrationBuilder.DropColumn(name: "Language", table: "Tickets");
            migrationBuilder.DropColumn(name: "OsVersion", table: "Tickets");
            migrationBuilder.DropColumn(name: "Phone", table: "Tickets");
            migrationBuilder.DropColumn(name: "Screen", table: "Tickets");
            migrationBuilder.DropColumn(name: "Timezone", table: "Tickets");
        }
    }
}
