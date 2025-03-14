using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackubJobPlanRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupJobs_BackupPlans_BackupPlanId1",
                table: "BackupJobs");

            migrationBuilder.DropIndex(
                name: "IX_BackupJobs_BackupPlanId1",
                table: "BackupJobs");

            migrationBuilder.DropColumn(
                name: "BackupPlanId1",
                table: "BackupJobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BackupPlanId1",
                table: "BackupJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_BackupPlanId1",
                table: "BackupJobs",
                column: "BackupPlanId1");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupJobs_BackupPlans_BackupPlanId1",
                table: "BackupJobs",
                column: "BackupPlanId1",
                principalTable: "BackupPlans",
                principalColumn: "Id");
        }
    }
}
