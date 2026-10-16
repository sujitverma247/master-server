using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MasterServer.Data;

#nullable disable

namespace MasterServer.Data.Migrations
{
    [DbContext(typeof(AppDb))]
    [Migration("20261016055200_TicketYearSequenceIds")]
    public partial class TicketYearSequenceIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH numbered AS (
                  SELECT
                    "Id",
                    EXTRACT(YEAR FROM "CreatedAt" AT TIME ZONE 'UTC')::int AS y,
                    ROW_NUMBER() OVER (
                      PARTITION BY EXTRACT(YEAR FROM "CreatedAt" AT TIME ZONE 'UTC')
                      ORDER BY "Id"
                    ) AS seq
                  FROM "Tickets"
                )
                UPDATE "Tickets" AS t
                SET "PublicId" = 'TIKC-'
                  || CASE WHEN n.seq < 1000 THEN LPAD(n.seq::text, 3, '0') ELSE n.seq::text END
                  || '-'
                  || n.y::text
                FROM numbered AS n
                WHERE t."Id" = n."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
