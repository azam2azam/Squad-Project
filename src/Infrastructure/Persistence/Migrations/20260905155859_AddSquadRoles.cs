using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSquadRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SquadRoles",
                columns: table => new
                {
                    Value = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PluralLabel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SquadRoles", x => x.Value);
                });

            migrationBuilder.InsertData(
                table: "SquadRoles",
                columns: new[] { "Value", "Color", "IsActive", "IsBuiltIn", "Label", "Name", "OrderIndex", "PluralLabel" },
                values: new object[,]
                {
                    { 0, "#2DD4BF", true, true, "Product Owner", "ProductOwner", 0, "Product Owners" },
                    { 1, "#A78BFA", true, true, "Tech Lead", "TechLead", 1, "Tech Leads" },
                    { 2, "#6366F1", true, true, "Developer", "Developer", 2, "Developers" },
                    { 3, "#F59E0B", true, true, "QA Engineer", "QaEngineer", 3, "QA Engineers" },
                    { 4, "#EC4899", true, true, "UI/UX Designer", "UxDesigner", 4, "UI/UX Designers" },
                    { 5, "#38BDF8", true, true, "Business Analyst", "BusinessAnalyst", 5, "Business Analysts" },
                    { 6, "#10B981", true, true, "DevOps", "DevOps", 6, "DevOps" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SquadRoles_Name",
                table: "SquadRoles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SquadRoles");
        }
    }
}
