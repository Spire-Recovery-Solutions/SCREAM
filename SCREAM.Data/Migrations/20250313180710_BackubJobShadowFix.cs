using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackubJobShadowFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupItem_BackupPlans_BackupPlanId",
                table: "BackupItem");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupItem_BackupPlans_BackupPlanId1",
                table: "BackupItem");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupItemStatuses_BackupItem_BackupItemId",
                table: "BackupItemStatuses");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupPlans_DatabaseConnections_DatabaseConnectionId1",
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

            migrationBuilder.DropPrimaryKey(
                name: "PK_BackupItem",
                table: "BackupItem");

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

            migrationBuilder.RenameTable(
                name: "BackupItem",
                newName: "BackupItems");

            migrationBuilder.RenameIndex(
                name: "IX_BackupItem_BackupPlanId",
                table: "BackupItems",
                newName: "IX_BackupItems_BackupPlanId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BackupItems",
                table: "BackupItems",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupItems_BackupPlans_BackupPlanId",
                table: "BackupItems",
                column: "BackupPlanId",
                principalTable: "BackupPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BackupItemStatuses_BackupItems_BackupItemId",
                table: "BackupItemStatuses",
                column: "BackupItemId",
                principalTable: "BackupItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupItems_BackupPlans_BackupPlanId",
                table: "BackupItems");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupItemStatuses_BackupItems_BackupItemId",
                table: "BackupItemStatuses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_BackupItems",
                table: "BackupItems");

            migrationBuilder.RenameTable(
                name: "BackupItems",
                newName: "BackupItem");

            migrationBuilder.RenameIndex(
                name: "IX_BackupItems_BackupPlanId",
                table: "BackupItem",
                newName: "IX_BackupItem_BackupPlanId");

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

            migrationBuilder.AddPrimaryKey(
                name: "PK_BackupItem",
                table: "BackupItem",
                column: "Id");

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
                name: "FK_BackupItemStatuses_BackupItem_BackupItemId",
                table: "BackupItemStatuses",
                column: "BackupItemId",
                principalTable: "BackupItem",
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
                name: "FK_BackupPlans_StorageTargets_StorageTargetId1",
                table: "BackupPlans",
                column: "StorageTargetId1",
                principalTable: "StorageTargets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
