using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Metadata.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionneLesNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "record_version",
                table: "lookups",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "record_version",
                table: "lookups");
        }
    }
}
