using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MasterServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Keys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "Licenses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerEmail",
                table: "Licenses",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExternalCustomerId",
                table: "Licenses",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalPaymentId",
                table: "Licenses",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubscriptionId",
                table: "Licenses",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSubscription",
                table: "Licenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Licenses",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Licenses",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "KeyBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    PlanType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    MaxMachines = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    GeneratedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyBatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_BatchId",
                table: "Licenses",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_CustomerEmail",
                table: "Licenses",
                column: "CustomerEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ExternalSubscriptionId",
                table: "Licenses",
                column: "ExternalSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_KeyBatches_ProductId",
                table: "KeyBatches",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_Licenses_KeyBatches_BatchId",
                table: "Licenses",
                column: "BatchId",
                principalTable: "KeyBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Licenses_KeyBatches_BatchId",
                table: "Licenses");

            migrationBuilder.DropTable(
                name: "KeyBatches");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_BatchId",
                table: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_CustomerEmail",
                table: "Licenses");

            migrationBuilder.DropIndex(
                name: "IX_Licenses_ExternalSubscriptionId",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "CustomerEmail",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "ExternalCustomerId",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "ExternalPaymentId",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "ExternalSubscriptionId",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "IsSubscription",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Licenses");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Licenses");
        }
    }
}
