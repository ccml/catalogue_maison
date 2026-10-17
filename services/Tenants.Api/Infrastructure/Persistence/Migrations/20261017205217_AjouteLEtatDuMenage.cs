using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tenants.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AjouteLEtatDuMenage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "households",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Active");

            // Les ménages déjà inscrits ont leur schéma : ils sont actifs. La
            // valeur par défaut ne sert qu'à eux, et disparaît aussitôt — un
            // ménage ne doit jamais devenir actif par omission.
            migrationBuilder.Sql("ALTER TABLE households ALTER COLUMN status DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "status",
                table: "households");
        }
    }
}
