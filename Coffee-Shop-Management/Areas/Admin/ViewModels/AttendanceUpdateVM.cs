using System.ComponentModel.DataAnnotations;

namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class AttendanceUpdateVM
    {
        [Required]
        public int AssignmentId { get; set; }

        // Cho phép quản lý sửa ngày làm việc (nếu cần)
        [Required]
        public string WorkDate { get; set; } = string.Empty; // Format yyyy-MM-dd

        // Cho phép quản lý sửa giờ ca làm (nếu cần)
        [Required]
        public string AssignedStartTime { get; set; } = string.Empty; // Format HH:mm
        [Required]
        public string AssignedEndTime { get; set; } = string.Empty; // Format HH:mm

        // Thông tin chấm công có thể sửa
        public string? ActualClockIn { get; set; } // Format HH:mm or null/empty
        public string? ActualClockOut { get; set; } // Format HH:mm or null/empty
        public string AttendanceStatus { get; set; } = string.Empty;

        // Thông tin lương có thể sửa
        public decimal? WageRateSnapshot { get; set; }
        public decimal? WorkedHours { get; set; }
        public decimal? FinalWage { get; set; }

        // Thông tin duyệt
        [Required]
        public string ActionType { get; set; } = string.Empty; // "approve" or "reject"
        public string? ApprovalNote { get; set; }
    }
}
