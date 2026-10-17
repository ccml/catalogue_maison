using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Metadata.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialisationDuCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lookups",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    record = table.Column<string>(type: "jsonb", nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lookups", x => x.code);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lookups");
        }
    }
}
