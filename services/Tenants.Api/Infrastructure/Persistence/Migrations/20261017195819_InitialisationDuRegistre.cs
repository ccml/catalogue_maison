using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tenants.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialisationDuRegistre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "households",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_households", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "members",
                columns: table => new
                {
                    subject = table.Column<Guid>(type: "uuid", nullable: false),
                    household_id = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    username = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_members", x => x.subject);
                    table.ForeignKey(
                        name: "fk_members_households_household_id",
                        column: x => x.household_id,
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_members_household_id",
                table: "members",
                column: "household_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "members");

            migrationBuilder.DropTable(
                name: "households");
        }
    }
}
