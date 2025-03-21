using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReworkRestoreEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Engine",
                table: "BackupItems");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "BackupItems");

            migrationBuilder.DropColumn(
                name: "Schema",
                table: "BackupItems");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "BackupItems");

            migrationBuilder.RenameColumn(
                name: "RowCount",
                table: "BackupItems",
                newName: "RestorePlanId");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSelected",
                table: "BackupItems",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "INTEGER",
                oldDefaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "DatabaseItemId",
                table: "BackupItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "DatabaseItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Schema = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    RowCount = table.Column<long>(type: "INTEGER", nullable: true),
                    Engine = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestorePlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    DatabaseConnectionId = table.Column<long>(type: "INTEGER", nullable: false),
                    StorageTargetId = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceBackupJobId = table.Column<long>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    OverwriteExisting = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestorePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestorePlans_BackupJobs_SourceBackupJobId",
                        column: x => x.SourceBackupJobId,
                        principalTable: "BackupJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RestorePlans_DatabaseConnections_DatabaseConnectionId",
                        column: x => x.DatabaseConnectionId,
                        principalTable: "DatabaseConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RestorePlans_StorageTargets_StorageTargetId",
                        column: x => x.StorageTargetId,
                        principalTable: "StorageTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestoreSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaxAutoRetries = table.Column<int>(type: "INTEGER", nullable: false),
                    OverwriteExistingByDefault = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    UseParallelExecution = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    ImportTimeout = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3600),
                    SendEmailNotifications = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    NotificationEmail = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestoreJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RestorePlanId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsCompressed = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    IsEncrypted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestoreJobs_RestorePlans_RestorePlanId",
                        column: x => x.RestorePlanId,
                        principalTable: "RestorePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RestoreItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DatabaseItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RestoreJobId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestoreItems_DatabaseItems_DatabaseItemId",
                        column: x => x.DatabaseItemId,
                        principalTable: "DatabaseItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RestoreItems_RestoreJobs_RestoreJobId",
                        column: x => x.RestoreJobId,
                        principalTable: "RestoreJobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RestoreJobLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RestoreJobId = table.Column<long>(type: "INTEGER", nullable: false),
                    RestoreItemId = table.Column<long>(type: "INTEGER", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreJobLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestoreJobLogs_RestoreItems_RestoreItemId",
                        column: x => x.RestoreItemId,
                        principalTable: "RestoreItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RestoreJobLogs_RestoreJobs_RestoreJobId",
                        column: x => x.RestoreJobId,
                        principalTable: "RestoreJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupItems_DatabaseItemId",
                table: "BackupItems",
                column: "DatabaseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupItems_RestorePlanId",
                table: "BackupItems",
                column: "RestorePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreItems_DatabaseItemId",
                table: "RestoreItems",
                column: "DatabaseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreItems_RestoreJobId",
                table: "RestoreItems",
                column: "RestoreJobId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreJobLogs_RestoreItemId",
                table: "RestoreJobLogs",
                column: "RestoreItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreJobLogs_RestoreJobId",
                table: "RestoreJobLogs",
                column: "RestoreJobId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreJobs_RestorePlanId",
                table: "RestoreJobs",
                column: "RestorePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_RestorePlans_DatabaseConnectionId",
                table: "RestorePlans",
                column: "DatabaseConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_RestorePlans_SourceBackupJobId",
                table: "RestorePlans",
                column: "SourceBackupJobId");

            migrationBuilder.CreateIndex(
                name: "IX_RestorePlans_StorageTargetId",
                table: "RestorePlans",
                column: "StorageTargetId");

            migrationBuilder.AddForeignKey(
                name: "FK_BackupItems_DatabaseItems_DatabaseItemId",
                table: "BackupItems",
                column: "DatabaseItemId",
                principalTable: "DatabaseItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BackupItems_RestorePlans_RestorePlanId",
                table: "BackupItems",
                column: "RestorePlanId",
                principalTable: "RestorePlans",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupItems_DatabaseItems_DatabaseItemId",
                table: "BackupItems");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupItems_RestorePlans_RestorePlanId",
                table: "BackupItems");

            migrationBuilder.DropTable(
                name: "RestoreJobLogs");

            migrationBuilder.DropTable(
                name: "RestoreSettings");

            migrationBuilder.DropTable(
                name: "RestoreItems");

            migrationBuilder.DropTable(
                name: "DatabaseItems");

            migrationBuilder.DropTable(
                name: "RestoreJobs");

            migrationBuilder.DropTable(
                name: "RestorePlans");

            migrationBuilder.DropIndex(
                name: "IX_BackupItems_DatabaseItemId",
                table: "BackupItems");

            migrationBuilder.DropIndex(
                name: "IX_BackupItems_RestorePlanId",
                table: "BackupItems");

            migrationBuilder.DropColumn(
                name: "DatabaseItemId",
                table: "BackupItems");

            migrationBuilder.RenameColumn(
                name: "RestorePlanId",
                table: "BackupItems",
                newName: "RowCount");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSelected",
                table: "BackupItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "Engine",
                table: "BackupItems",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "BackupItems",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Schema",
                table: "BackupItems",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "BackupItems",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }
    }
}
