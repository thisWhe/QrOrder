using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrOrder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOrderingEnabled",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "TableSessionHours",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 12);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOrderingEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TableSessionHours",
                table: "Tenants");
        }
    }
}
