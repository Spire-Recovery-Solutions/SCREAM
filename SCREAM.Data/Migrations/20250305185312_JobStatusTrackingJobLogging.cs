using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class JobStatusTrackingJobLogging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupItemStatuses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BackupJobId = table.Column<long>(type: "INTEGER", nullable: false),
                    BackupItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupItemStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupItemStatuses_BackupItem_BackupItemId",
                        column: x => x.BackupItemId,
                        principalTable: "BackupItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BackupItemStatuses_BackupJobs_BackupJobId",
                        column: x => x.BackupJobId,
                        principalTable: "BackupJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BackupSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaxAutoRetries = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3),
                    BackupHistoryRetentionDays = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 30),
                    DefaultMaxAllowedPacket = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "64M"),
                    SendEmailNotifications = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    NotificationEmail = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupJobLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BackupJobId = table.Column<long>(type: "INTEGER", nullable: false),
                    BackupItemStatusId = table.Column<long>(type: "INTEGER", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupJobLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupJobLogs_BackupItemStatuses_BackupItemStatusId",
                        column: x => x.BackupItemStatusId,
                        principalTable: "BackupItemStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BackupJobLogs_BackupJobs_BackupJobId",
                        column: x => x.BackupJobId,
                        principalTable: "BackupJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupItemStatuses_BackupItemId",
                table: "BackupItemStatuses",
                column: "BackupItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupItemStatuses_BackupJobId",
                table: "BackupItemStatuses",
                column: "BackupJobId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobLogs_BackupItemStatusId",
                table: "BackupJobLogs",
                column: "BackupItemStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobLogs_BackupJobId",
                table: "BackupJobLogs",
                column: "BackupJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupJobLogs");

            migrationBuilder.DropTable(
                name: "BackupSettings");

            migrationBuilder.DropTable(
                name: "BackupItemStatuses");
        }
    }
}
