using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "About",
                table: "People",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Headline",
                table: "People",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "BoardAuditEntries",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "PersonAchievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AchievedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonAchievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonAchievements_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ByteCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonPhotos_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonSkills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonSkills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonSkills_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonAchievements_PersonId_OrderIndex",
                table: "PersonAchievements",
                columns: new[] { "PersonId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonPhotos_PersonId",
                table: "PersonPhotos",
                column: "PersonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonSkills_Name",
                table: "PersonSkills",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PersonSkills_PersonId_OrderIndex",
                table: "PersonSkills",
                columns: new[] { "PersonId", "OrderIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonAchievements");

            migrationBuilder.DropTable(
                name: "PersonPhotos");

            migrationBuilder.DropTable(
                name: "PersonSkills");

            migrationBuilder.DropColumn(
                name: "About",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Headline",
                table: "People");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "BoardAuditEntries",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }
    }
}
