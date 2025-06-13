using System.ComponentModel.DataAnnotations;

namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class ShiftEditVM
    {
        [Required]
        public int ShiftId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập giờ bắt đầu.")]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Định dạng giờ bắt đầu không hợp lệ (HH:mm).")]
        public string StartTime { get; set; } = string.Empty; // Nhận dạng chuỗi HH:mm

        [Required(ErrorMessage = "Vui lòng nhập giờ kết thúc.")]
        [RegularExpression(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Định dạng giờ kết thúc không hợp lệ (HH:mm).")]
        public string EndTime { get; set; } = string.Empty;   // Nhận dạng chuỗi HH:mm
    }
}
