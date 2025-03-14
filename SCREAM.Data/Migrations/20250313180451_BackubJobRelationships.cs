using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackubJobRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupItem_BackupPlans_BackupPlanId",
                table: "BackupItem");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupPlans_DatabaseConnections_DatabaseConnectionId",
                table: "BackupPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupPlans_StorageTargets_StorageTargetId",
                table: "BackupPlans");

            migrationBuilder.DropTable(
                name: "BackupSchedules");

            migrationBuilder.AddColumn<long>(
                name: "DatabaseConnectionId1",
                table: "BackupPlans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "StorageTargetId1",
                table: "BackupPlans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "BackupPlanId1",
                table: "BackupItem",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupPlans_DatabaseConnectionId1",
                table: "BackupPlans",
                column: "DatabaseConnectionId1");

            migrationBuilder.CreateIndex(
                name: "IX_BackupPlans_StorageTargetId1",
                table: "BackupPlans",
                column: "StorageTargetId1");

            migrationBuilder.CreateIndex(
                name: "IX_BackupItem_BackupPlanId1",
                table: "BackupItem",
                column: "BackupPlanId1");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupItem_BackupPlans_BackupPlanId",
                table: "BackupItem",
                column: "BackupPlanId",
                principalTable: "BackupPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BackupItem_BackupPlans_BackupPlanId1",
                table: "BackupItem",
                column: "BackupPlanId1",
                principalTable: "BackupPlans",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupPlans_DatabaseConnections_DatabaseConnectionId",
                table: "BackupPlans",
                column: "DatabaseConnectionId",
                principalTable: "DatabaseConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BackupPlans_DatabaseConnections_DatabaseConnectionId1",
                table: "BackupPlans",
                column: "DatabaseConnectionId1",
                principalTable: "DatabaseConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BackupPlans_StorageTargets_StorageTargetId",
                table: "BackupPlans",
                column: "StorageTargetId",
                principalTable: "StorageTargets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BackupPlans_StorageTargets_StorageTargetId1",
                table: "BackupPlans",
                column: "StorageTargetId1",
                principalTable: "StorageTargets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupItem_BackupPlans_BackupPlanId",
                table: "BackupItem");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupItem_BackupPlans_BackupPlanId1",
                table: "BackupItem");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupPlans_DatabaseConnections_DatabaseConnectionId",
                table: "BackupPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupPlans_DatabaseConnections_DatabaseConnectionId1",
                table: "BackupPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupPlans_StorageTargets_StorageTargetId",
                table: "BackupPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupPlans_StorageTargets_StorageTargetId1",
                table: "BackupPlans");

            migrationBuilder.DropIndex(
                name: "IX_BackupPlans_DatabaseConnectionId1",
                table: "BackupPlans");

            migrationBuilder.DropIndex(
                name: "IX_BackupPlans_StorageTargetId1",
                table: "BackupPlans");

            migrationBuilder.DropIndex(
                name: "IX_BackupItem_BackupPlanId1",
                table: "BackupItem");

            migrationBuilder.DropColumn(
                name: "DatabaseConnectionId1",
                table: "BackupPlans");

            migrationBuilder.DropColumn(
                name: "StorageTargetId1",
                table: "BackupPlans");

            migrationBuilder.DropColumn(
                name: "BackupPlanId1",
                table: "BackupItem");

            migrationBuilder.CreateTable(
                name: "BackupSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BackupPlanId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CronExpression = table.Column<string>(type: "TEXT", nullable: false),
                    LastRun = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRun = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScheduledType = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupSchedules_BackupPlans_BackupPlanId",
                        column: x => x.BackupPlanId,
                        principalTable: "BackupPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupSchedules_BackupPlanId",
                table: "BackupSchedules",
                column: "BackupPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupItem_BackupPlans_BackupPlanId",
                table: "BackupItem",
                column: "BackupPlanId",
                principalTable: "BackupPlans",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupPlans_DatabaseConnections_DatabaseConnectionId",
                table: "BackupPlans",
                column: "DatabaseConnectionId",
                principalTable: "DatabaseConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BackupPlans_StorageTargets_StorageTargetId",
                table: "BackupPlans",
                column: "StorageTargetId",
                principalTable: "StorageTargets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
