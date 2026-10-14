using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalogue.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialisationDuCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    creator = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    location = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_lent = table.Column<bool>(type: "boolean", nullable: false),
                    specifics = table.Column<string>(type: "jsonb", nullable: true),
                    isbn = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, computedColumnSql: "specifics ->> 'isbn'", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medias", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_medias_added_at",
                table: "medias",
                column: "added_at");

            migrationBuilder.CreateIndex(
                name: "ix_medias_isbn",
                table: "medias",
                column: "isbn");

            migrationBuilder.CreateIndex(
                name: "ix_medias_type",
                table: "medias",
                column: "type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medias");
        }
    }
}
