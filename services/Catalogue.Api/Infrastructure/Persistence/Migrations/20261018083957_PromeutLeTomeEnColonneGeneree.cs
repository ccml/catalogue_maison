using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalogue.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromeutLeTomeEnColonneGeneree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_medias_series",
                table: "medias");

            migrationBuilder.AddColumn<int>(
                name: "volume",
                table: "medias",
                type: "integer",
                nullable: true,
                computedColumnSql: "(specifics ->> 'volume')::integer",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_medias_series_volume",
                table: "medias",
                columns: new[] { "series", "volume" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_medias_series_volume",
                table: "medias");

            migrationBuilder.DropColumn(
                name: "volume",
                table: "medias");

            migrationBuilder.CreateIndex(
                name: "ix_medias_series",
                table: "medias",
                column: "series");
        }
    }
}
