using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReworkRestorePlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupItems_RestorePlans_RestorePlanId",
                table: "BackupItems");

            migrationBuilder.DropForeignKey(
                name: "FK_RestoreItems_RestoreJobs_RestoreJobId",
                table: "RestoreItems");

            migrationBuilder.DropForeignKey(
                name: "FK_RestorePlans_StorageTargets_StorageTargetId",
                table: "RestorePlans");

            migrationBuilder.DropIndex(
                name: "IX_RestorePlans_StorageTargetId",
                table: "RestorePlans");

            migrationBuilder.DropIndex(
                name: "IX_BackupItems_RestorePlanId",
                table: "BackupItems");

            migrationBuilder.DropColumn(
                name: "StorageTargetId",
                table: "RestorePlans");

            migrationBuilder.DropColumn(
                name: "RestorePlanId",
                table: "BackupItems");

            migrationBuilder.AlterColumn<long>(
                name: "RestoreJobId",
                table: "RestoreItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "RestorePlanBackupItem",
                columns: table => new
                {
                    RestorePlanId = table.Column<long>(type: "INTEGER", nullable: false),
                    BackupItemId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestorePlanBackupItem", x => new { x.BackupItemId, x.RestorePlanId });
                    table.ForeignKey(
                        name: "FK_RestorePlanBackupItem_BackupItems_BackupItemId",
                        column: x => x.BackupItemId,
                        principalTable: "BackupItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RestorePlanBackupItem_RestorePlans_RestorePlanId",
                        column: x => x.RestorePlanId,
                        principalTable: "RestorePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RestorePlanBackupItem_RestorePlanId",
                table: "RestorePlanBackupItem",
                column: "RestorePlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_RestoreItems_RestoreJobs_RestoreJobId",
                table: "RestoreItems",
                column: "RestoreJobId",
                principalTable: "RestoreJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RestoreItems_RestoreJobs_RestoreJobId",
                table: "RestoreItems");

            migrationBuilder.DropTable(
                name: "RestorePlanBackupItem");

            migrationBuilder.AddColumn<long>(
                name: "StorageTargetId",
                table: "RestorePlans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<long>(
                name: "RestoreJobId",
                table: "RestoreItems",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<long>(
                name: "RestorePlanId",
                table: "BackupItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestorePlans_StorageTargetId",
                table: "RestorePlans",
                column: "StorageTargetId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupItems_RestorePlanId",
                table: "BackupItems",
                column: "RestorePlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupItems_RestorePlans_RestorePlanId",
                table: "BackupItems",
                column: "RestorePlanId",
                principalTable: "RestorePlans",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RestoreItems_RestoreJobs_RestoreJobId",
                table: "RestoreItems",
                column: "RestoreJobId",
                principalTable: "RestoreJobs",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RestorePlans_StorageTargets_StorageTargetId",
                table: "RestorePlans",
                column: "StorageTargetId",
                principalTable: "StorageTargets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
