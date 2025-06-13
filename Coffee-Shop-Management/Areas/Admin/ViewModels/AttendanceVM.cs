namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class AttendanceVM
    {
        public int AssignmentId { get; set; }
        public string WorkDateFormatted { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string ShiftInfo { get; set; } = string.Empty; // vd: "07:00-11:00"
        public string ClockInFormatted { get; set; } = string.Empty; // vd: "07:05" hoặc ""
        public string ClockOutFormatted { get; set; } = string.Empty; // vd: "11:15" hoặc ""
        public string AttendanceStatus { get; set; } = string.Empty;
        public string FinalWageFormatted { get; set; } = string.Empty; // vd: "125,100 đ"
        public string ApprovalStatus { get; set; } = string.Empty;
        public string ActionsHtml { get; set; } = string.Empty; // HTML cho cột Hành động
        // Thêm các trường ẩn nếu cần cho filtering/sorting phía client (ít dùng với server-side processing)
        // public DateTime WorkDate { get; set; }
        // public decimal? FinalWageValue { get; set; }
    }
}
