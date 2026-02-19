using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normyx.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActionReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionReviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionReviews_ActionItemId_ReviewedAt",
                table: "ActionReviews",
                columns: new[] { "ActionItemId", "ReviewedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionReviews");
        }
    }
}
