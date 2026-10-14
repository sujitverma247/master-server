using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MasterServer.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Telemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "Activations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Heartbeats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicenseId = table.Column<int>(type: "integer", nullable: false),
                    ActivationId = table.Column<int>(type: "integer", nullable: false),
                    MachineId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OsVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Heartbeats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activations_LastSeenAt",
                table: "Activations",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_Heartbeats_LicenseId",
                table: "Heartbeats",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_Heartbeats_MachineId",
                table: "Heartbeats",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_Heartbeats_SeenAt",
                table: "Heartbeats",
                column: "SeenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Heartbeats");

            migrationBuilder.DropIndex(
                name: "IX_Activations_LastSeenAt",
                table: "Activations");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "Activations");
        }
    }
}
