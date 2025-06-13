using System.ComponentModel.DataAnnotations;

namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class LoginVM
    {
        [Required(ErrorMessage = "⚠ Vui lòng nhập địa chỉ tên người dùng.")]
        //[EmailAddress(ErrorMessage = "⚠ Địa chỉ email không hợp lệ.")] // Kiểm tra định dạng email
        [StringLength(100, ErrorMessage = "⚠ Địa chỉ tên người dùng không được vượt quá 100 ký tự.")]
        public string? Username { get; set; }

        [Required(ErrorMessage = "⚠ Vui lòng nhập mật khẩu.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "⚠ Mật khẩu phải có ít nhất 6 ký tự.")] // Giới hạn độ dài, tối thiểu 6 ký tự
        [DataType(DataType.Password)]  // Chỉ định kiểu dữ liệu là mật khẩu (ẩn ký tự)
        public string? Password { get; set; }

        public bool RememberMe { get; set; }
    }
}
