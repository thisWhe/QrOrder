using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrOrder.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TableSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceCalls_TableSessions_TableSessionId",
                        column: x => x.TableSessionId,
                        principalTable: "TableSessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ServiceCalls_Tables_TableId",
                        column: x => x.TableId,
                        principalTable: "Tables",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCalls_TableId",
                table: "ServiceCalls",
                column: "TableId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCalls_TableSessionId",
                table: "ServiceCalls",
                column: "TableSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCalls_TenantId_Status_CreatedAt",
                table: "ServiceCalls",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceCalls");
        }
    }
}
