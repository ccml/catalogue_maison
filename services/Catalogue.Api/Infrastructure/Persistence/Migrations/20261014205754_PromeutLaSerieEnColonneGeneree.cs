using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalogue.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromeutLaSerieEnColonneGeneree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "series",
                table: "medias",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                computedColumnSql: "specifics ->> 'series'",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_medias_series",
                table: "medias",
                column: "series");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_medias_series",
                table: "medias");

            migrationBuilder.DropColumn(
                name: "series",
                table: "medias");
        }
    }
}
