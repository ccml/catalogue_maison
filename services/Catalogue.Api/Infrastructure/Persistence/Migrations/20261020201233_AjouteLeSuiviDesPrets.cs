using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalogue.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AjouteLeSuiviDesPrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    share_id = table.Column<Guid>(type: "uuid", nullable: true),
                    borrower_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loans", x => x.id);
                    table.ForeignKey(
                        name: "fk_loans_medias_media_id",
                        column: x => x.media_id,
                        principalTable: "medias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loans_media_id_active",
                table: "loans",
                column: "media_id",
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_loans_media_id_status",
                table: "loans",
                columns: new[] { "media_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_loans_share_id_status",
                table: "loans",
                columns: new[] { "share_id", "status" });

            // Chaque média déjà marqué prêté reçoit son prêt en cours,
            // d'emprunteur inconnu : is_lent reste vrai exactement quand un
            // prêt est en cours, et aucune donnée saisie n'est perdue. Sans
            // schéma qualifié : la migration se rejoue sous le search_path de
            // chaque ménage.
            migrationBuilder.Sql("""
                INSERT INTO loans (id, media_id, status, borrower_name)
                SELECT gen_random_uuid(), id, 'Active', 'Inconnu'
                FROM medias
                WHERE is_lent;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loans");
        }
    }
}
