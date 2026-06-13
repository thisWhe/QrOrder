using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrOrder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductServingTemperature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServingTemperature",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServingTemperature",
                table: "Products");
        }
    }
}
