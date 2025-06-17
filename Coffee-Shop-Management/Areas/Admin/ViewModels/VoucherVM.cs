using static Coffee_Shop_Management.Models.AppDbContext;
using System.ComponentModel.DataAnnotations;

namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class VoucherVM
    {
        [Required]
        public InventoryTransactionType TransactionType { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        public int? SupplierId { get; set; } // Chỉ dùng cho loại phiếu Nhập Mua Hàng

        [Required]
        [MinLength(1, ErrorMessage = "Phiếu phải có ít nhất một nguyên vật liệu.")]
        public List<VoucherDetailVM> Details { get; set; } = new List<VoucherDetailVM>();
    }
}
