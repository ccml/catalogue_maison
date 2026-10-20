using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tenants.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AjouteLePartageEntreMenages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "share_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_household_id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    code_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_share_invitations_households_owner_household_id",
                        column: x => x.owner_household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_household_id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    viewer_household_id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shares", x => x.id);
                    table.CheckConstraint("ck_shares_not_self", "owner_household_id <> viewer_household_id");
                    table.ForeignKey(
                        name: "fk_shares_households_owner_household_id",
                        column: x => x.owner_household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shares_households_viewer_household_id",
                        column: x => x.viewer_household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_share_invitations_code_hash",
                table: "share_invitations",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_share_invitations_owner_household_id",
                table: "share_invitations",
                column: "owner_household_id");

            migrationBuilder.CreateIndex(
                name: "ix_shares_owner_household_id_viewer_household_id",
                table: "shares",
                columns: new[] { "owner_household_id", "viewer_household_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shares_viewer_household_id",
                table: "shares",
                column: "viewer_household_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "share_invitations");

            migrationBuilder.DropTable(
                name: "shares");
        }
    }
}
