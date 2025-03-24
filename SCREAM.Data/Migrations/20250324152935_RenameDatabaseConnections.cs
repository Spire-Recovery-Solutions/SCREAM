using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameDatabaseConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupPlans_DatabaseConnections_DatabaseConnectionId",
                table: "BackupPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_RestorePlans_DatabaseConnections_DatabaseConnectionId",
                table: "RestorePlans");

            migrationBuilder.DropTable(
                name: "DatabaseConnections");

            migrationBuilder.RenameColumn(
                name: "DatabaseConnectionId",
                table: "RestorePlans",
                newName: "DatabaseTargetId");

            migrationBuilder.RenameIndex(
                name: "IX_RestorePlans_DatabaseConnectionId",
                table: "RestorePlans",
                newName: "IX_RestorePlans_DatabaseTargetId");

            migrationBuilder.RenameColumn(
                name: "DatabaseConnectionId",
                table: "BackupPlans",
                newName: "DatabaseTargetId");

            migrationBuilder.RenameIndex(
                name: "IX_BackupPlans_DatabaseConnectionId",
                table: "BackupPlans",
                newName: "IX_BackupPlans_DatabaseTargetId");

            migrationBuilder.CreateTable(
                name: "DatabaseTargets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HostName = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", nullable: false),
                    Password = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseTargets", x => x.Id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_BackupPlans_DatabaseTargets_DatabaseTargetId",
                table: "BackupPlans",
                column: "DatabaseTargetId",
                principalTable: "DatabaseTargets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RestorePlans_DatabaseTargets_DatabaseTargetId",
                table: "RestorePlans",
                column: "DatabaseTargetId",
                principalTable: "DatabaseTargets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BackupPlans_DatabaseTargets_DatabaseTargetId",
                table: "BackupPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_RestorePlans_DatabaseTargets_DatabaseTargetId",
                table: "RestorePlans");

            migrationBuilder.DropTable(
                name: "DatabaseTargets");

            migrationBuilder.RenameColumn(
                name: "DatabaseTargetId",
                table: "RestorePlans",
                newName: "DatabaseConnectionId");

            migrationBuilder.RenameIndex(
                name: "IX_RestorePlans_DatabaseTargetId",
                table: "RestorePlans",
                newName: "IX_RestorePlans_DatabaseConnectionId");

            migrationBuilder.RenameColumn(
                name: "DatabaseTargetId",
                table: "BackupPlans",
                newName: "DatabaseConnectionId");

            migrationBuilder.RenameIndex(
                name: "IX_BackupPlans_DatabaseTargetId",
                table: "BackupPlans",
                newName: "IX_BackupPlans_DatabaseConnectionId");

            migrationBuilder.CreateTable(
                name: "DatabaseConnections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    HostName = table.Column<string>(type: "TEXT", nullable: false),
                    Password = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", rowVersion: true, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UserName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseConnections", x => x.Id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_BackupPlans_DatabaseConnections_DatabaseConnectionId",
                table: "BackupPlans",
                column: "DatabaseConnectionId",
                principalTable: "DatabaseConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RestorePlans_DatabaseConnections_DatabaseConnectionId",
                table: "RestorePlans",
                column: "DatabaseConnectionId",
                principalTable: "DatabaseConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
