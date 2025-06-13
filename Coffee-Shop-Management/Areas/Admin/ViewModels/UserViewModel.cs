using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class UserViewModel
    {
        public string? Id { get; set; } // Dùng string cho Id của IdentityUser

        [Required(ErrorMessage = "Tên đăng nhập là bắt buộc.")]
        [Display(Name = "Tên đăng nhập")]
        public string UserName { get; set; } = "";

        [Required(ErrorMessage = "Email là bắt buộc.")]
        [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
        [Display(Name = "Họ và tên")]
        [StringLength(100)]
        public string Name { get; set; } = "";

        [Display(Name = "Số điện thoại")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string? PhoneNumber { get; set; }

        [Display(Name = "Địa chỉ")]
        [StringLength(400)]
        public string? Address { get; set; }

        [Display(Name = "Chức vụ")]
        [StringLength(100)]
        public string? JobTitle { get; set; }

        [Display(Name = "Ngày sinh")]
        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }

        [Display(Name = "Mức lương (giờ)")]
        [Range(0, double.MaxValue, ErrorMessage = "Mức lương không hợp lệ.")]
        [DataType(DataType.Currency)] // Giúp định dạng hiển thị
        [Column(TypeName = "decimal(18, 2)")] // Chỉ định kiểu dữ liệu trong DB nếu cần
        public decimal? WageRate { get; set; }

        [Display(Name = "Ảnh đại diện")]
        public string? ImageUrl { get; set; } // Lưu đường dẫn ảnh hiện tại

        [Display(Name = "Chọn ảnh mới")]
        public IFormFile? AvatarFile { get; set; } // Dùng để upload file mới

        [Display(Name = "Khóa tài khoản")]
        public bool IsLocked { get; set; } = false; // Trạng thái khóa

        // --- Password Fields (Chỉ bắt buộc khi tạo mới hoặc khi nhập để thay đổi) ---
        [DataType(DataType.Password)]
        [StringLength(100, ErrorMessage = "{0} phải dài từ {2} đến {1} ký tự.", MinimumLength = 6)]
        [Display(Name = "Mật khẩu mới")]
        // Không cần Required ở đây vì chỉ bắt buộc khi tạo hoặc khi người dùng muốn đổi
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Xác nhận mật khẩu")]
        [Compare("Password", ErrorMessage = "Mật khẩu và mật khẩu xác nhận không khớp.")]
        public string? ConfirmPassword { get; set; }
        // --- End Password Fields ---


        [Display(Name = "Vai trò")]
        public List<string> SelectedRoles { get; set; } = new List<string>(); // Danh sách role IDs/Names được chọn

        // Thuộc tính này chỉ dùng để truyền dữ liệu từ Controller -> View cho Select2
        public List<string>? AllRoles { get; set; } // Danh sách tất cả các Role có thể chọn

        // Thuộc tính chỉ đọc để hiển thị ngày tạo (nếu cần)
        [Display(Name = "Ngày tạo")]
        public DateTime? CreatedAt { get; set; }
    }
}
