using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackupJobsEtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BackupSchedules_BackupPlanId",
                table: "BackupSchedules");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRun",
                table: "BackupPlans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRun",
                table: "BackupPlans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleCron",
                table: "BackupPlans",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ScheduleType",
                table: "BackupPlans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "BackupPlanId1",
                table: "BackupJobs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupSchedules_BackupPlanId",
                table: "BackupSchedules",
                column: "BackupPlanId");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupJobs_BackupPlans_BackupPlanId1",
                table: "BackupJobs");

            migrationBuilder.DropIndex(
                name: "IX_BackupSchedules_BackupPlanId",
                table: "BackupSchedules");

            migrationBuilder.DropIndex(
                name: "IX_BackupJobs_BackupPlanId1",
                table: "BackupJobs");

            migrationBuilder.DropColumn(
                name: "LastRun",
                table: "BackupPlans");

            migrationBuilder.DropColumn(
                name: "NextRun",
                table: "BackupPlans");

            migrationBuilder.DropColumn(
                name: "ScheduleCron",
                table: "BackupPlans");

            migrationBuilder.DropColumn(
                name: "ScheduleType",
                table: "BackupPlans");

            migrationBuilder.DropColumn(
                name: "BackupPlanId1",
                table: "BackupJobs");

            migrationBuilder.CreateIndex(
                name: "IX_BackupSchedules_BackupPlanId",
                table: "BackupSchedules",
                column: "BackupPlanId",
                unique: true);
        }
    }
}
