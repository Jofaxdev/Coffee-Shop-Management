using System.ComponentModel.DataAnnotations;

namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class VoucherDetailVM
    {
        [Required]
        public int IngredientId { get; set; }

        [Required]
        [Range(0.0001, double.MaxValue)]
        public decimal Quantity { get; set; }

        public decimal? UnitPrice { get; set; }
        public int? BatchId { get; set; }
        public int? SupplierId { get; set; }
    }
}
