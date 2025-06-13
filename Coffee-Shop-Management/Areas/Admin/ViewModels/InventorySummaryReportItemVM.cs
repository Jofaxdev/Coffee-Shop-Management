namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class InventorySummaryReportItemVM
    {
        public int STT { get; set; }
        public string IngredientCode { get; set; } // Mã hàng
        public string IngredientName { get; set; } // Tên hàng
        public string UnitOfMeasure { get; set; }  // ĐVT
        public decimal? PurchasePrice { get; set; } // Giá nhập (cần xác định cách lấy giá này: giá cuối, giá trung bình?)
        public decimal? CostPrice { get; set; }     // Giá vốn (LastPurchasePrice của Ingredient có thể dùng ở đây)

        public decimal OpeningStockQuantity { get; set; }
        public decimal OpeningStockValue { get; set; }

        public decimal PeriodQuantityIn { get; set; }
        public decimal PeriodValueIn { get; set; } // Tiền chi cho NVL trong kỳ

        public decimal PeriodQuantityOut { get; set; }
        public decimal PeriodValueOut { get; set; } // Giá trị vốn hàng xuất

        public decimal ClosingStockQuantity { get; set; }
        public decimal ClosingStockValue { get; set; }
    }
}
