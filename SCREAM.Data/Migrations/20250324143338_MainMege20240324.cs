using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class MainMege20240324 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RestorePlans_BackupJobs_SourceBackupJobId",
                table: "RestorePlans");

            migrationBuilder.RenameColumn(
                name: "SourceBackupJobId",
                table: "RestorePlans",
                newName: "SourceBackupPlanId");

            migrationBuilder.RenameIndex(
                name: "IX_RestorePlans_SourceBackupJobId",
                table: "RestorePlans",
                newName: "IX_RestorePlans_SourceBackupPlanId");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRun",
                table: "RestorePlans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRun",
                table: "RestorePlans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleCron",
                table: "RestorePlans",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ScheduleType",
                table: "RestorePlans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasTriggeredRestore",
                table: "BackupJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_RestorePlans_BackupPlans_SourceBackupPlanId",
                table: "RestorePlans",
                column: "SourceBackupPlanId",
                principalTable: "BackupPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RestorePlans_BackupPlans_SourceBackupPlanId",
                table: "RestorePlans");

            migrationBuilder.DropColumn(
                name: "LastRun",
                table: "RestorePlans");

            migrationBuilder.DropColumn(
                name: "NextRun",
                table: "RestorePlans");

            migrationBuilder.DropColumn(
                name: "ScheduleCron",
                table: "RestorePlans");

            migrationBuilder.DropColumn(
                name: "ScheduleType",
                table: "RestorePlans");

            migrationBuilder.DropColumn(
                name: "HasTriggeredRestore",
                table: "BackupJobs");

            migrationBuilder.RenameColumn(
                name: "SourceBackupPlanId",
                table: "RestorePlans",
                newName: "SourceBackupJobId");

            migrationBuilder.RenameIndex(
                name: "IX_RestorePlans_SourceBackupPlanId",
                table: "RestorePlans",
                newName: "IX_RestorePlans_SourceBackupJobId");

            migrationBuilder.AddForeignKey(
                name: "FK_RestorePlans_BackupJobs_SourceBackupJobId",
                table: "RestorePlans",
                column: "SourceBackupJobId",
                principalTable: "BackupJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
