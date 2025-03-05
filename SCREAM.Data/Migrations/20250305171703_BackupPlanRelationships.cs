using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackupPlanRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupPlans_BackupSchedules_ScheduleId",
                table: "BackupPlans");

            migrationBuilder.DropIndex(
                name: "IX_BackupPlans_ScheduleId",
                table: "BackupPlans");

            migrationBuilder.RenameColumn(
                name: "ScheduleId",
                table: "BackupPlans",
                newName: "IsActive");

            migrationBuilder.AddColumn<long>(
                name: "BackupPlanId",
                table: "BackupSchedules",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "BackupJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlanId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupJobs_BackupPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "BackupPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupSchedules_BackupPlanId",
                table: "BackupSchedules",
                column: "BackupPlanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_PlanId",
                table: "BackupJobs",
                column: "PlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupSchedules_BackupPlans_BackupPlanId",
                table: "BackupSchedules",
                column: "BackupPlanId",
                principalTable: "BackupPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupSchedules_BackupPlans_BackupPlanId",
                table: "BackupSchedules");

            migrationBuilder.DropTable(
                name: "BackupJobs");

            migrationBuilder.DropIndex(
                name: "IX_BackupSchedules_BackupPlanId",
                table: "BackupSchedules");

            migrationBuilder.DropColumn(
                name: "BackupPlanId",
                table: "BackupSchedules");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "BackupPlans",
                newName: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupPlans_ScheduleId",
                table: "BackupPlans",
                column: "ScheduleId");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupPlans_BackupSchedules_ScheduleId",
                table: "BackupPlans",
                column: "ScheduleId",
                principalTable: "BackupSchedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
