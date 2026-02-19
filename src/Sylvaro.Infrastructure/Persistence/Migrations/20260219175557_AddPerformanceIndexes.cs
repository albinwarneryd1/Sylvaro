using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Normyx.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Findings_AssessmentId",
                table: "Findings");

            migrationBuilder.DropIndex(
                name: "IX_Assessments_AiSystemVersionId",
                table: "Assessments");

            migrationBuilder.DropIndex(
                name: "IX_ActionItems_AiSystemVersionId",
                table: "ActionItems");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_AssessmentId_Severity",
                table: "Findings",
                columns: new[] { "AssessmentId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportArtifacts_TenantId_CreatedAt",
                table: "ExportArtifacts",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_ActionType_Timestamp",
                table: "AuditLogs",
                columns: new[] { "TenantId", "ActionType", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_ActorUserId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "TenantId", "ActorUserId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_AiSystemVersionId_RanAt",
                table: "Assessments",
                columns: new[] { "AiSystemVersionId", "RanAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_AiSystemVersionId_Status_DueDate",
                table: "ActionItems",
                columns: new[] { "AiSystemVersionId", "Status", "DueDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_ActionReviews_ActionItems_ActionItemId",
                table: "ActionReviews",
                column: "ActionItemId",
                principalTable: "ActionItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActionReviews_ActionItems_ActionItemId",
                table: "ActionReviews");

            migrationBuilder.DropIndex(
                name: "IX_Findings_AssessmentId_Severity",
                table: "Findings");

            migrationBuilder.DropIndex(
                name: "IX_ExportArtifacts_TenantId_CreatedAt",
                table: "ExportArtifacts");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId_ActionType_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId_ActorUserId_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Assessments_AiSystemVersionId_RanAt",
                table: "Assessments");

            migrationBuilder.DropIndex(
                name: "IX_ActionItems_AiSystemVersionId_Status_DueDate",
                table: "ActionItems");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_AssessmentId",
                table: "Findings",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_AiSystemVersionId",
                table: "Assessments",
                column: "AiSystemVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_AiSystemVersionId",
                table: "ActionItems",
                column: "AiSystemVersionId");
        }
    }
}
