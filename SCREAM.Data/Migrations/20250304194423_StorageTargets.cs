using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SCREAM.Data.Migrations
{
    /// <inheritdoc />
    public partial class StorageTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorageTargets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ConnectionString = table.Column<string>(type: "TEXT", nullable: true),
                    ContainerName = table.Column<string>(type: "TEXT", nullable: true),
                    GoogleCloudStorageTarget_BucketName = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceAccountKey = table.Column<string>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", nullable: true),
                    BucketName = table.Column<string>(type: "TEXT", nullable: true),
                    AccessKey = table.Column<string>(type: "TEXT", nullable: true),
                    SecretKey = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageTargets", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageTargets");
        }
    }
}
