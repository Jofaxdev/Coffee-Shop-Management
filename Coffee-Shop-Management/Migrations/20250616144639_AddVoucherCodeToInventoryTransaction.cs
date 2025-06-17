using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coffee_Shop_Management.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherCodeToInventoryTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoucherCode",
                table: "InventoryTransactions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoucherCode",
                table: "InventoryTransactions");
        }
    }
}
