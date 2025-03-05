using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanupRefactor20250305 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupJobs_BackupPlans_PlanId",
                table: "BackupJobs");

            migrationBuilder.RenameColumn(
                name: "PlanId",
                table: "BackupJobs",
                newName: "BackupPlanId");

            migrationBuilder.RenameIndex(
                name: "IX_BackupJobs_PlanId",
                table: "BackupJobs",
                newName: "IX_BackupJobs_BackupPlanId");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "StorageTargets",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StorageTargets",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "DatabaseConnections",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "DatabaseConnections",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "BackupSchedules",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "BackupSchedules",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "BackupPlans",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "BackupPlans",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "BackupJobs",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "BackupJobs",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "BackupItem",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "BackupItem",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupJobs_BackupPlans_BackupPlanId",
                table: "BackupJobs",
                column: "BackupPlanId",
                principalTable: "BackupPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupJobs_BackupPlans_BackupPlanId",
                table: "BackupJobs");

            migrationBuilder.RenameColumn(
                name: "BackupPlanId",
                table: "BackupJobs",
                newName: "PlanId");

            migrationBuilder.RenameIndex(
                name: "IX_BackupJobs_BackupPlanId",
                table: "BackupJobs",
                newName: "IX_BackupJobs_PlanId");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "StorageTargets",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true,
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "StorageTargets",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "DatabaseConnections",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true,
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "DatabaseConnections",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "BackupSchedules",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true,
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "BackupSchedules",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "BackupPlans",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true,
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "BackupPlans",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "BackupJobs",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true,
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "BackupJobs",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "BackupItem",
                type: "TEXT",
                rowVersion: true,
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldRowVersion: true,
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "BackupItem",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupJobs_BackupPlans_PlanId",
                table: "BackupJobs",
                column: "PlanId",
                principalTable: "BackupPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
