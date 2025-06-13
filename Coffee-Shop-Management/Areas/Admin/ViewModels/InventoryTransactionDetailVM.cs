namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class InventoryTransactionDetailVM
    {
        public DateTime TransactionDate { get; set; }
        public long TransactionId { get; set; }
        public string TransactionTypeDisplay { get; set; }
        public string UserName { get; set; }
        public string SupplierName { get; set; }
        public decimal? QuantityIn { get; set; }
        public decimal? UnitPriceIn { get; set; }
        public decimal? ValueIn { get; set; }
        public decimal? QuantityOut { get; set; }
        public decimal? UnitPriceOut { get; set; }
        public decimal? ValueOut { get; set; }
        public decimal StockAfterTransaction { get; set; }
        public string Notes { get; set; }
    }
}
