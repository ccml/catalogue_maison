using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalogue.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromeutLeCodeBarresEnColonneGeneree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "barcode",
                table: "medias",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true,
                computedColumnSql: "specifics ->> 'barcode'",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_medias_barcode",
                table: "medias",
                column: "barcode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_medias_barcode",
                table: "medias");

            migrationBuilder.DropColumn(
                name: "barcode",
                table: "medias");
        }
    }
}
