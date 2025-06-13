using static Coffee_Shop_Management.Areas.Admin.Controllers.InventoryController;

namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class InventoryDetailReportItemVM
    {
        public int IngredientId { get; set; }
        public string IngredientCode { get; set; } // Giả sử có mã NVL
        public string IngredientName { get; set; }
        public string UnitOfMeasure { get; set; }
        public decimal? MinimumStockLevel { get; set; }

        public DateTime PeriodStartDate { get; set; }
        public DateTime PeriodEndDate { get; set; }

        public decimal OpeningStockQuantity { get; set; }
        public decimal OpeningStockUnitPrice { get; set; }
        public decimal OpeningStockValue => OpeningStockQuantity * OpeningStockUnitPrice;

        public List<InventoryTransactionDetailVM> Transactions { get; set; } = new List<InventoryTransactionDetailVM>();

        public decimal TotalQuantityIn => Transactions.Where(t => t.QuantityIn.HasValue).Sum(t => t.QuantityIn.Value);
        public decimal TotalValueIn => Transactions.Where(t => t.ValueIn.HasValue).Sum(t => t.ValueIn.Value);
        public decimal TotalQuantityOut => Transactions.Where(t => t.QuantityOut.HasValue).Sum(t => t.QuantityOut.Value);
        public decimal TotalValueOut => Transactions.Where(t => t.ValueOut.HasValue).Sum(t => t.ValueOut.Value);

        public decimal ClosingStockQuantity { get; set; }
        public decimal ClosingStockUnitPrice { get; set; }
        public decimal ClosingStockValue => ClosingStockQuantity * ClosingStockUnitPrice;
        public decimal? QuantityToOrder => MinimumStockLevel.HasValue && MinimumStockLevel.Value > ClosingStockQuantity
                                            ? MinimumStockLevel.Value - ClosingStockQuantity
                                            : 0; // Hiển thị 0 nếu không cần đặt thêm hoặc không có mức tồn tối thiểu
    }
}
