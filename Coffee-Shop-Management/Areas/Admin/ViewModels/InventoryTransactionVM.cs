using static Coffee_Shop_Management.Models.AppDbContext;
using System.ComponentModel.DataAnnotations;

namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class InventoryTransactionVM
    {
        public long Id { get; set; } // Thường không cần cho form tạo mới, nhưng có thể hữu ích

        [Required(ErrorMessage = "Vui lòng chọn nguyên vật liệu.")]
        [Display(Name = "Nguyên vật liệu")]
        public int IngredientId { get; set; }

        // Các thuộc tính sau dùng để hiển thị trên form, không nhất thiết phải post lại
        public string? IngredientName { get; set; }
        public string? IngredientUnit { get; set; }
        public decimal IngredientCurrentStock { get; set; }


        [Required(ErrorMessage = "Vui lòng chọn loại giao dịch.")]
        [Display(Name = "Loại giao dịch")]
        public InventoryTransactionType TransactionType { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập ngày giao dịch.")]
        [DataType(DataType.DateTime)]
        [Display(Name = "Ngày giao dịch")]
        public DateTime TransactionDate { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "Vui lòng nhập số lượng.")]
        [Range(0.00001, double.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
        [Display(Name = "Số lượng")]
        public decimal Quantity { get; set; } // UI luôn nhập số dương

        [Display(Name = "Đơn giá")]
        // Đơn giá có thể là null tùy loại giao dịch, validation sẽ được kiểm tra trong controller
        // dựa trên TransactionType. Ví dụ: bắt buộc cho Purchase, InitialStock.
        [Range(0, double.MaxValue, ErrorMessage = "Đơn giá không hợp lệ.")]
        public decimal? UnitPrice { get; set; }

        [Display(Name = "Thành tiền")]
        [Range(0, double.MaxValue, ErrorMessage = "Thành tiền không hợp lệ.")]
        public decimal? TotalPrice { get; set; } // Có thể được tính toán tự động

        [Display(Name = "Nhà cung cấp")]
        // SupplierId có thể là null tùy loại giao dịch, validation sẽ được kiểm tra trong controller.
        // Ví dụ: bắt buộc cho Purchase.
        public int? SupplierId { get; set; }
        public string? SupplierName { get; set; } // Dùng để hiển thị


        [MaxLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
        [Display(Name = "Ghi chú")]
        public string? Notes { get; set; }

        [Display(Name = "Lô hàng")]
        public int? BatchId { get; set; } // ID của lô hàng được chọn khi xuất/giảm
    }
}
