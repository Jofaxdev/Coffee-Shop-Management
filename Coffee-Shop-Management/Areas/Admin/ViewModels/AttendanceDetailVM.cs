namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class AttendanceDetailVM
    {
        public int AssignmentId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string? AssignedByUserName { get; set; }
        public string? AssignmentCreatedAtFormatted { get; set; } // dd/MM/yyyy HH:mm:ss

        // Editable fields in Modal
        public string WorkDate { get; set; } = string.Empty; // Format yyyy-MM-dd
        public string AssignedStartTime { get; set; } = string.Empty; // Format HH:mm
        public string AssignedEndTime { get; set; } = string.Empty; // Format HH:mm
        public string? ActualClockIn { get; set; } // Format HH:mm or null/empty
        public string? ActualClockOut { get; set; } // Format HH:mm or null/empty
        public string AttendanceStatus { get; set; } = string.Empty;
        public decimal? WageRateSnapshot { get; set; }
        public decimal? WorkedHours { get; set; }
        public decimal? FinalWage { get; set; }
        public string? ApprovalNote { get; set; }

        // Readonly approval info in Modal
        public string ApprovalStatus { get; set; } = string.Empty;
        public string? ApprovedByUserName { get; set; }
        public string? ApprovedAtFormatted { get; set; } // dd/MM/yyyy HH:mm:ss
    }
}
