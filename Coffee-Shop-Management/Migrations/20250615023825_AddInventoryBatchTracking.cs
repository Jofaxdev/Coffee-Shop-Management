using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coffee_Shop_Management.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryBatchTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IngredientId = table.Column<int>(type: "int", nullable: false),
                    BatchNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchaseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryBatches_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_BatchId",
                table: "InventoryTransactions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBatches_IngredientId",
                table: "InventoryBatches",
                column: "IngredientId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_InventoryBatches_BatchId",
                table: "InventoryTransactions",
                column: "BatchId",
                principalTable: "InventoryBatches",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_InventoryBatches_BatchId",
                table: "InventoryTransactions");

            migrationBuilder.DropTable(
                name: "InventoryBatches");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_BatchId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "InventoryTransactions");
        }
    }
}
