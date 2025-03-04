using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackupPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BackupPlanId",
                table: "BackupItem",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BackupSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CronExpression = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduledType = table.Column<string>(type: "TEXT", nullable: false),
                    LastRun = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRun = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupPlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    DatabaseConnectionId = table.Column<long>(type: "INTEGER", nullable: false),
                    StorageTargetId = table.Column<long>(type: "INTEGER", nullable: false),
                    ScheduleId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupPlans_BackupSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "BackupSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BackupPlans_DatabaseConnections_DatabaseConnectionId",
                        column: x => x.DatabaseConnectionId,
                        principalTable: "DatabaseConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BackupPlans_StorageTargets_StorageTargetId",
                        column: x => x.StorageTargetId,
                        principalTable: "StorageTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupItem_BackupPlanId",
                table: "BackupItem",
                column: "BackupPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupPlans_DatabaseConnectionId",
                table: "BackupPlans",
                column: "DatabaseConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupPlans_ScheduleId",
                table: "BackupPlans",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupPlans_StorageTargetId",
                table: "BackupPlans",
                column: "StorageTargetId");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupItem_BackupPlans_BackupPlanId",
                table: "BackupItem",
                column: "BackupPlanId",
                principalTable: "BackupPlans",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupItem_BackupPlans_BackupPlanId",
                table: "BackupItem");

            migrationBuilder.DropTable(
                name: "BackupPlans");

            migrationBuilder.DropTable(
                name: "BackupSchedules");

            migrationBuilder.DropIndex(
                name: "IX_BackupItem_BackupPlanId",
                table: "BackupItem");

            migrationBuilder.DropColumn(
                name: "BackupPlanId",
                table: "BackupItem");
        }
    }
}
