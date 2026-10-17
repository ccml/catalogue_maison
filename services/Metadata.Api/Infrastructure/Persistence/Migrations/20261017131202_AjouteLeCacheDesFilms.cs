using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Metadata.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AjouteLeCacheDesFilms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "films",
                columns: table => new
                {
                    imdb_id = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    record = table.Column<string>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    record_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_films", x => x.imdb_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "films");
        }
    }
}
