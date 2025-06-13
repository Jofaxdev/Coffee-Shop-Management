using System.ComponentModel.DataAnnotations;

namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class ProductVM
    {
        [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
        [StringLength(100, ErrorMessage = "Tên sản phẩm không được vượt quá 100 ký tự.")]
        public string NameProduct { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng chọn loại sản phẩm.")]
        [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn loại sản phẩm hợp lệ.")]
        public int IdCategory { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập hoặc chọn đơn vị tính.")]
        [StringLength(50, ErrorMessage = "Đơn vị tính không được vượt quá 50 ký tự.")]
        public string Unit { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập giá vốn.")]
        [Range(0, (double)decimal.MaxValue, ErrorMessage = "Giá vốn phải là số không âm.")]
        [DataType(DataType.Currency)]
        public decimal OriginalPrice { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập đơn giá.")]
        [Range(0, (double)decimal.MaxValue, ErrorMessage = "Đơn giá phải là số không âm.")]
        [DataType(DataType.Currency)]
        public decimal Price { get; set; }

        [StringLength(500, ErrorMessage = "Mô tả không được vượt quá 500 ký tự.")]
        public string? Description { get; set; } // Cho phép null

        // Ảnh không bắt buộc ở đây, sẽ kiểm tra trong Controller khi Create
        public IFormFile? ImageProduct { get; set; } // Cho phép null

        [Required(ErrorMessage = "Vui lòng chọn trạng thái.")]
        public string InStock { get; set; } = "true"; // Giá trị mặc định
    }
}