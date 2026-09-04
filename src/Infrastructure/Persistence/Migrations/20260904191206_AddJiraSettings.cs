using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJiraSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JiraSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    EncryptedApiToken = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    TokenHint = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    AutoApply = table.Column<bool>(type: "bit", nullable: false),
                    SyncIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSyncAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSyncResult = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JiraSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JiraSettings");
        }
    }
}
