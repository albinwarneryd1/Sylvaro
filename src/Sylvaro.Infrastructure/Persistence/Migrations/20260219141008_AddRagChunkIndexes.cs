using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normyx.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRagChunkIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RagChunks_TenantId_DocumentId",
                table: "RagChunks",
                columns: new[] { "TenantId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_RagChunks_TenantId_SourceType",
                table: "RagChunks",
                columns: new[] { "TenantId", "SourceType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RagChunks_TenantId_DocumentId",
                table: "RagChunks");

            migrationBuilder.DropIndex(
                name: "IX_RagChunks_TenantId_SourceType",
                table: "RagChunks");
        }
    }
}
