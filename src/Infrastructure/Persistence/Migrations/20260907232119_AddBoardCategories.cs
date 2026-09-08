using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Boards",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BoardCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Boards_CategoryId",
                table: "Boards",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BoardCategories_Name",
                table: "BoardCategories",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Boards_BoardCategories_CategoryId",
                table: "Boards",
                column: "CategoryId",
                principalTable: "BoardCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Boards_BoardCategories_CategoryId",
                table: "Boards");

            migrationBuilder.DropTable(
                name: "BoardCategories");

            migrationBuilder.DropIndex(
                name: "IX_Boards_CategoryId",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Boards");
        }
    }
}
