using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Boards",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "BoardAuditEntries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TelegramEnrolments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RedeemedByTelegramUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramEnrolments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelegramEnrolments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelegramLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramUsername = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelegramLinks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelegramMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    SenderTelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    SenderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BoardId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelegramSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    EncryptedBotToken = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    TokenHint = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    BotUsername = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdateId = table.Column<long>(type: "bigint", nullable: false),
                    AllowedChatIds = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReplyToUnknownSenders = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastPollAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastPollResult = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Boards_Code",
                table: "Boards",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramEnrolments_Code",
                table: "TelegramEnrolments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramEnrolments_UserId",
                table: "TelegramEnrolments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramLinks_TelegramUserId",
                table: "TelegramLinks",
                column: "TelegramUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramLinks_UserId",
                table: "TelegramLinks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramMessages_ReceivedAt",
                table: "TelegramMessages",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramMessages_UpdateId",
                table: "TelegramMessages",
                column: "UpdateId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramEnrolments");

            migrationBuilder.DropTable(
                name: "TelegramLinks");

            migrationBuilder.DropTable(
                name: "TelegramMessages");

            migrationBuilder.DropTable(
                name: "TelegramSettings");

            migrationBuilder.DropIndex(
                name: "IX_Boards_Code",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "BoardAuditEntries");
        }
    }
}
