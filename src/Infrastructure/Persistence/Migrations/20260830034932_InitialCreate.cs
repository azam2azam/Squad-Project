using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoardAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Boards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Product = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SquadName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Sprint = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProgressPercent = table.Column<int>(type: "int", nullable: false),
                    BlockerNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Velocity = table.Column<double>(type: "float", nullable: true),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    JiraProjectKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    JiraBoardId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefaultRole = table.Column<int>(type: "int", nullable: false),
                    DefaultDetail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    AvatarColorOverride = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SquadMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AllocationPercent = table.Column<int>(type: "int", nullable: true),
                    OrderIndex = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquadMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SquadMembers_Boards_BoardId",
                        column: x => x.BoardId,
                        principalTable: "Boards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SquadMembers_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoardAuditEntries_BoardId_ChangedAt",
                table: "BoardAuditEntries",
                columns: new[] { "BoardId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Boards_IsDeleted",
                table: "Boards",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Boards_OrderIndex",
                table: "Boards",
                column: "OrderIndex");

            migrationBuilder.CreateIndex(
                name: "IX_People_FullName",
                table: "People",
                column: "FullName");

            migrationBuilder.CreateIndex(
                name: "IX_People_IsActive",
                table: "People",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SquadMembers_BoardId_OrderIndex",
                table: "SquadMembers",
                columns: new[] { "BoardId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_SquadMembers_BoardId_PersonId",
                table: "SquadMembers",
                columns: new[] { "BoardId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SquadMembers_PersonId",
                table: "SquadMembers",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoardAuditEntries");

            migrationBuilder.DropTable(
                name: "SquadMembers");

            migrationBuilder.DropTable(
                name: "Boards");

            migrationBuilder.DropTable(
                name: "People");
        }
    }
}
