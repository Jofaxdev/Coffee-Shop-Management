using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Coffee_Shop_Management.Areas.Admin.ViewModels; // Đảm bảo namespace đúng
using Microsoft.Extensions.Logging;
using static Coffee_Shop_Management.Models.AppDbContext;
using ClosedXML.Excel;

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    // [Authorize(Roles = "Admin,Manager")] 
    public class AttendanceController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly ILogger<AttendanceController> _logger;
        private readonly CultureInfo _vnCulture = new CultureInfo("vi-VN");

        public AttendanceController(AppDbContext context, UserManager<AppUser> userManager, ILogger<AttendanceController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // GET: /Admin/Attendance/
        public IActionResult Index()
        {
            return View();
        }

        // GET: /Admin/Attendance/GetShifts
        [HttpGet]
        public async Task<IActionResult> GetShifts()
        {
            try
            {
                var shifts = await _context.Shifts
                                        .OrderBy(s => s.StartTime) // Sắp xếp theo giờ bắt đầu
                                        .Select(s => new { s.ShiftId, s.Name }) // Chỉ lấy ID và Tên
                                        .ToListAsync();
                return Ok(shifts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách ca làm.");
                return StatusCode(500, "Lỗi máy chủ khi lấy danh sách ca làm.");
            }
        }


        // POST: /Admin/Attendance/GetAttendanceData
        [HttpPost]
        public async Task<IActionResult> GetAttendanceData()
        {
            try
            {
                _logger.LogInformation("Bắt đầu GetAttendanceData");

                var draw = Request.Form["draw"].FirstOrDefault();
                var start = Request.Form["start"].FirstOrDefault();
                var length = Request.Form["length"].FirstOrDefault();
                var orderColumnIndex = Request.Form["order[0][column]"].FirstOrDefault();
                var sortColumn = Request.Form[$"columns[{orderColumnIndex}][name]"].FirstOrDefault();
                var sortColumnDirection = Request.Form["order[0][dir]"].FirstOrDefault();
                var searchValue = Request.Form["search[value]"].FirstOrDefault() ?? "";

                // --- Lấy các bộ lọc tùy chỉnh MỚI ---
                var fromDateStr = Request.Form["fromDate"].FirstOrDefault();
                var toDateStr = Request.Form["toDate"].FirstOrDefault();
                var approvalStatusFilter = Request.Form["approvalStatusFilter"].FirstOrDefault();
                var attendanceStatusFilter = Request.Form["attendanceStatusFilter"].FirstOrDefault(); // Filter Trạng thái CC
                var shiftFilter = Request.Form["shiftFilter"].FirstOrDefault(); // Filter Ca làm (sẽ là ShiftId)

                int pageSize = (int.TryParse(length, out int len) && len > 0) ? len : 10;
                if (pageSize == -1) pageSize = int.MaxValue;
                int skip = (int.TryParse(start, out int st)) ? st : 0;

                var query = _context.EmployeeAssignments
                                    .Include(a => a.Employee)
                                    .Include(a => a.Shift)
                                    .AsQueryable();

                // --- Áp dụng bộ lọc ---
                if (DateTime.TryParseExact(fromDateStr, "d/M/yyyy", _vnCulture, DateTimeStyles.None, out var fromDate))
                { query = query.Where(a => a.WorkDate >= fromDate); }
                if (DateTime.TryParseExact(toDateStr, "d/M/yyyy", _vnCulture, DateTimeStyles.None, out var toDate))
                { query = query.Where(a => a.WorkDate <= toDate); }

                if (!string.IsNullOrEmpty(approvalStatusFilter))
                { query = query.Where(a => a.ApprovalStatus == approvalStatusFilter); }

                // *** THÊM LỌC THEO TRẠNG THÁI CHẤM CÔNG ***
                if (!string.IsNullOrEmpty(attendanceStatusFilter))
                { query = query.Where(a => a.AttendanceStatus == attendanceStatusFilter); }

                // *** THÊM LỌC THEO CA LÀM (ShiftId) ***
                if (int.TryParse(shiftFilter, out int shiftId) && shiftId > 0) // Kiểm tra có phải số nguyên > 0 không
                { query = query.Where(a => a.ShiftId == shiftId); }

                // Lọc tìm kiếm chung 
                if (!string.IsNullOrEmpty(searchValue))
                {
                    var searchValueLower = searchValue.ToLower();
                    query = query.Where(a =>
                        (a.Employee != null && a.Employee.Name != null && a.Employee.Name.ToLower().Contains(searchValueLower)) ||
                        (a.Shift != null && a.Shift.Name != null && a.Shift.Name.ToLower().Contains(searchValueLower)) ||
                        (a.AttendanceStatus != null && a.AttendanceStatus.ToLower().Contains(searchValueLower)) ||
                        (a.ApprovalStatus != null && a.ApprovalStatus.ToLower().Contains(searchValueLower))
                    // Đã bỏ tìm theo ngày/giờ định dạng
                    );
                }

                int recordsFiltered = await query.CountAsync();

                // --- Sắp xếp ---
                if (!string.IsNullOrEmpty(sortColumn) && !string.IsNullOrEmpty(sortColumnDirection))
                {
                    Expression<Func<EmployeeAssignment, object>> keySelector = sortColumn.ToLower() switch
                    {
                        "workdate" => e => e.WorkDate,
                        "employeename" => e => e.Employee == null ? "" : (e.Employee.Name ?? ""),
                        "shiftinfo" => e => e.Shift == null ? "" : (e.Shift.StartTime.ToString(@"hh\:mm") + "-" + e.Shift.EndTime.ToString(@"hh\:mm")),
                        "clockin" => e => (DateTime?)e.ActualClockIn,
                        "clockout" => e => (DateTime?)e.ActualClockOut,
                        "attendancestatus" => e => e.AttendanceStatus ?? "",
                        "finalwage" => e => e.FinalWage ?? 0,
                        "approvalstatus" => e => e.ApprovalStatus ?? "",
                        _ => e => e.WorkDate
                    };

                    query = sortColumnDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
                          ? query.OrderByDescending(keySelector)
                          : query.OrderBy(keySelector);
                }
                else
                { query = query.OrderByDescending(e => e.WorkDate).ThenBy(e => e.Shift != null ? e.Shift.StartTime : TimeSpan.Zero); }

                // --- Phân trang ---
                var pagedData = await query.Skip(skip).Take(pageSize).ToListAsync();

                // --- Chuyển đổi sang ViewModel ---
                var viewModelData = pagedData.Select(a => new AttendanceVM
                {
                    AssignmentId = a.AssignmentId,
                    WorkDateFormatted = a.WorkDate.ToString("dd/MM/yyyy"),
                    EmployeeName = a.Employee?.Name ?? "N/A",
                    ShiftInfo = a.Shift != null ? $"{a.Shift.StartTime:hh\\:mm}-{a.Shift.EndTime:hh\\:mm}" : "N/A",
                    ClockInFormatted = a.ActualClockIn?.ToString("HH:mm") ?? "",
                    ClockOutFormatted = a.ActualClockOut?.ToString("HH:mm") ?? "",
                    AttendanceStatus = a.AttendanceStatus ?? "",
                    FinalWageFormatted = (a.FinalWage ?? 0).ToString("N0", _vnCulture) + " đ",
                    ApprovalStatus = a.ApprovalStatus ?? ""
                }).ToList();

                int recordsTotal = await _context.EmployeeAssignments.CountAsync();

                // --- Trả về kết quả JSON ---
                var jsonData = new { draw, recordsFiltered, recordsTotal, data = viewModelData };
                _logger.LogInformation($"GetAttendanceData thành công. Trả về {viewModelData.Count} bản ghi.");
                return Ok(jsonData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong GetAttendanceData");
                return BadRequest(new { error = "Đã xảy ra lỗi khi tải dữ liệu chấm công. Vui lòng thử lại." });
            }
        }


        // GET: /Admin/Attendance/GetAssignmentDetail/5 (Giữ nguyên)
        [HttpGet]
        public async Task<IActionResult> GetAssignmentDetail(int id)
        {
            _logger.LogInformation($"Bắt đầu GetAssignmentDetail với ID: {id}");
            if (id <= 0)
            {
                _logger.LogWarning("GetAssignmentDetail nhận ID không hợp lệ: {Id}", id);
                return BadRequest(new { success = false, message = "ID chấm công không hợp lệ." });
            }
            try
            {
                var assignment = await _context.EmployeeAssignments
                    .Include(a => a.Employee).Include(a => a.Shift)
                    .Include(a => a.AssignedByUser).Include(a => a.ApprovedByUser)
                    .FirstOrDefaultAsync(a => a.AssignmentId == id);

                if (assignment == null)
                { return NotFound(new { success = false, message = "Không tìm thấy bản ghi chấm công." }); }

                var viewModel = new AttendanceDetailVM
                {
                    AssignmentId = assignment.AssignmentId,
                    EmployeeName = assignment.Employee?.Name ?? "N/A",
                    EmployeeId = assignment.EmployeeId,
                    AssignedByUserName = assignment.AssignedByUser?.Name ?? "Hệ thống",
                    AssignmentCreatedAtFormatted = assignment.AssignmentCreatedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                    WorkDate = assignment.WorkDate.ToString("yyyy-MM-dd"),
                    AssignedStartTime = assignment.AssignedStartTime.ToString(@"hh\:mm"),
                    AssignedEndTime = assignment.AssignedEndTime.ToString(@"hh\:mm"),
                    ActualClockIn = assignment.ActualClockIn?.ToString("HH:mm"),
                    ActualClockOut = assignment.ActualClockOut?.ToString("HH:mm"),
                    AttendanceStatus = assignment.AttendanceStatus ?? "",
                    WageRateSnapshot = assignment.WageRateSnapshot,
                    WorkedHours = assignment.WorkedHours,
                    FinalWage = assignment.FinalWage,
                    ApprovalNote = assignment.ApprovalNote,
                    ApprovalStatus = assignment.ApprovalStatus ?? "",
                    ApprovedByUserName = assignment.ApprovedByUser?.Name,
                    ApprovedAtFormatted = assignment.ApprovedAt?.ToString("dd/MM/yyyy HH:mm:ss")
                };
                _logger.LogInformation($"GetAssignmentDetail thành công cho ID: {id}");
                return Ok(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong GetAssignmentDetail cho ID: {Id}", id);
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi lấy chi tiết chấm công." });
            }
        }


        // POST: /Admin/Attendance/UpdateAttendance (Giữ nguyên)
        [HttpPost]
        public async Task<IActionResult> UpdateAttendance([FromBody] AttendanceUpdateVM model)
        {
            _logger.LogInformation($"Bắt đầu UpdateAttendance cho AssignmentId: {model?.AssignmentId}");
            if (model == null || model.AssignmentId <= 0)
            { return BadRequest(new { success = false, message = "Dữ liệu gửi lên không hợp lệ." }); }
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            var assignment = await _context.EmployeeAssignments
                                            .Include(a => a.Shift)
                                            .FirstOrDefaultAsync(a => a.AssignmentId == model.AssignmentId);
            if (assignment == null)
            { return NotFound(new { success = false, message = "Không tìm thấy bản ghi chấm công cần cập nhật." }); }

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            { return Unauthorized(new { success = false, message = "Yêu cầu đăng nhập để thực hiện." }); }
            // TODO: Kiểm tra quyền Manager/Admin

            try
            {
                if (DateTime.TryParseExact(model.WorkDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var workDate))
                { assignment.WorkDate = workDate; }
                else { _logger.LogWarning("Lỗi parse WorkDate '{WorkDate}' cho AssignmentId: {AssignmentId}", model.WorkDate, model.AssignmentId); }

                if (TimeSpan.TryParseExact(model.AssignedStartTime, @"hh\:mm", CultureInfo.InvariantCulture, out var assignedStart))
                { assignment.AssignedStartTime = assignedStart; }
                if (TimeSpan.TryParseExact(model.AssignedEndTime, @"hh\:mm", CultureInfo.InvariantCulture, out var assignedEnd))
                { assignment.AssignedEndTime = assignedEnd; }

                assignment.ActualClockIn = CombineDateTime(model.WorkDate, model.ActualClockIn);
                assignment.ActualClockOut = CombineDateTime(model.WorkDate, model.ActualClockOut);
                assignment.AttendanceStatus = model.AttendanceStatus ?? assignment.AttendanceStatus;
                assignment.WageRateSnapshot = model.WageRateSnapshot ?? assignment.WageRateSnapshot;
                assignment.WorkedHours = model.WorkedHours;
                assignment.FinalWage = model.FinalWage;
                assignment.ApprovalNote = model.ApprovalNote?.Trim();

                if (model.ActionType.Equals("approve", StringComparison.OrdinalIgnoreCase))
                {
                    assignment.ApprovalStatus = "Đã duyệt";
                    assignment.ApprovedByUserId = currentUser.Id;
                    assignment.ApprovedAt = DateTime.Now;
                }
                else if (model.ActionType.Equals("reject", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(model.ApprovalNote))
                    { return BadRequest(new { success = false, message = "Vui lòng nhập lý do từ chối." }); }
                    assignment.ApprovalStatus = "Từ chối";
                    assignment.ApprovedByUserId = currentUser.Id;
                    assignment.ApprovedAt = DateTime.Now;
                    assignment.FinalWage = 0;
                    assignment.WorkedHours = 0;
                }
                else
                { return BadRequest(new { success = false, message = "Hành động không được hỗ trợ." }); }

                _context.Entry(assignment).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                _logger.LogInformation($"UpdateAttendance thành công cho AssignmentId: {model.AssignmentId}, Action: {model.ActionType}");
                return Ok(new { success = true, message = $"Đã {(model.ActionType == "approve" ? "duyệt" : "từ chối")} bản ghi chấm công thành công!" });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Lỗi xung đột dữ liệu khi cập nhật AssignmentId: {AssignmentId}", model.AssignmentId);
                return Conflict(new { success = false, message = "Dữ liệu vừa được thay đổi bởi người khác. Vui lòng tải lại và thử lại." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi không xác định trong UpdateAttendance cho AssignmentId: {AssignmentId}", model.AssignmentId);
                return StatusCode(500, new { success = false, message = "Đã xảy ra lỗi máy chủ trong quá trình cập nhật." });
            }
        }

        // *** ACTION XUẤT EXCEL ĐÃ SỬA LỖI ĐỊNH DẠNG ***
        [HttpGet]
        public async Task<IActionResult> ExportPayrollExcel(string? fromDate, string? toDate, bool checkOnly = false)
        {
            _logger.LogInformation("Bắt đầu ExportPayrollExcel với fromDate={FromDate}, toDate={ToDate}, checkOnly={CheckOnly}", fromDate, toDate, checkOnly);

            try
            {
                DateTime startDate = DateTime.MinValue;
                DateTime endDate = DateTime.MaxValue;

                if (DateTime.TryParseExact(fromDate, "d/M/yyyy", _vnCulture, DateTimeStyles.None, out var parsedFromDate)) { startDate = parsedFromDate.Date; }
                if (DateTime.TryParseExact(toDate, "d/M/yyyy", _vnCulture, DateTimeStyles.None, out var parsedToDate)) { endDate = parsedToDate.Date.AddDays(1).AddTicks(-1); }

                // --- Bước 1: Luôn kiểm tra dữ liệu trước ---
                var approvedAssignments = await _context.EmployeeAssignments
                    .Where(a => a.ApprovalStatus == "Đã duyệt" && a.WorkDate >= startDate && a.WorkDate <= endDate)
                    // Chỉ cần kiểm tra sự tồn tại, không cần include hay order nếu chỉ check
                    .Select(a => a.AssignmentId) // Chỉ lấy ID để tối ưu
                    .Take(1) // Chỉ cần 1 bản ghi để biết là có dữ liệu
                    .ToListAsync();

                // Nếu không có dữ liệu
                if (!approvedAssignments.Any())
                {
                    _logger.LogWarning("Không tìm thấy dữ liệu chấm công đã duyệt trong khoảng {StartDate} - {EndDate}", startDate.ToString("dd/MM/yyyy"), endDate.ToString("dd/MM/yyyy"));
                    // Trả về NotFound (404) với thông báo JSON cho cả AJAX và request thường
                    return NotFound(new { success = false, message = "Không có dữ liệu chấm công đã duyệt trong khoảng thời gian đã chọn để xuất báo cáo." });
                }

                // --- Bước 2: Nếu là yêu cầu AJAX chỉ để kiểm tra ---
                if (checkOnly)
                {
                    _logger.LogInformation("Yêu cầu kiểm tra (checkOnly=true) thành công, có dữ liệu.");
                    // Trả về Ok (200) với thông báo thành công JSON
                    return Ok(new { success = true, message = "Dữ liệu hợp lệ, sẵn sàng để xuất file." });
                }

                // --- Bước 3: Nếu không phải checkOnly (tức là yêu cầu tải file thực sự) ---
                // Query lại đầy đủ dữ liệu để xuất file (vì query ở trên chỉ lấy 1 ID)
                var fullApprovedAssignments = await _context.EmployeeAssignments
                   .Where(a => a.ApprovalStatus == "Đã duyệt" && a.WorkDate >= startDate && a.WorkDate <= endDate)
                   .Include(a => a.Employee)
                   .Include(a => a.Shift)
                   .OrderBy(a => a.Employee.Name).ThenBy(a => a.WorkDate).ThenBy(a => a.Shift.StartTime)
                   .ToListAsync();

                // Group và tạo file Excel như cũ
                var groupedData = fullApprovedAssignments
                                   .GroupBy(a => new { a.EmployeeId, EmployeeName = a.Employee.Name ?? "Không xác định" })
                                   .Select(g => new {
                                       EmployeeId = g.Key.EmployeeId,
                                       EmployeeName = g.Key.EmployeeName,
                                       Assignments = g.ToList(),
                                       TotalWage = g.Sum(a => a.FinalWage ?? 0)
                                   })
                                   .OrderBy(g => g.EmployeeName)
                                   .ToList();

                // --- Tạo file Excel (Giữ nguyên logic tạo file) ---
                using (var workbook = new XLWorkbook())
                {
                    // ... (Toàn bộ code tạo worksheet, header, data, format... giữ nguyên như cũ) ...
                    var worksheet = workbook.Worksheets.Add("BaoCaoLuong");
                    worksheet.ColumnWidth = 15; // Set độ rộng mặc định

                    // --- Tiêu đề chính ---
                    var titleCell = worksheet.Cell("A1");
                    titleCell.Value = $"BÁO CÁO LƯƠNG NHÂN VIÊN TỪ {startDate:dd/MM/yyyy} ĐẾN {endDate:dd/MM/yyyy}";
                    titleCell.Style.Font.Bold = true;
                    titleCell.Style.Font.FontSize = 16;
                    titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    worksheet.Range("A1:I1").Merge(); // Merge 9 cột (A-I)

                    int currentRow = 3;

                    // --- Tiêu đề cột chi tiết ---
                    worksheet.Cell(currentRow, 1).Value = "STT";
                    worksheet.Cell(currentRow, 2).Value = "Ngày làm";
                    worksheet.Cell(currentRow, 3).Value = "Ca làm";
                    worksheet.Cell(currentRow, 4).Value = "Giờ vào";
                    worksheet.Cell(currentRow, 5).Value = "Giờ ra";
                    worksheet.Cell(currentRow, 6).Value = "Số giờ";
                    worksheet.Cell(currentRow, 7).Value = "Lương/giờ (VND)"; // Ghi rõ đơn vị ở header
                    worksheet.Cell(currentRow, 8).Value = "Thành tiền ca (VND)"; // Ghi rõ đơn vị ở header
                    worksheet.Cell(currentRow, 9).Value = "Ghi chú duyệt";

                    var headerRange = worksheet.Range(currentRow, 1, currentRow, 9);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
                    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    headerRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                    headerRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
                    worksheet.Row(currentRow).Height = 20; // Tăng chiều cao header
                    currentRow++;

                    // --- Dữ liệu chi tiết từng nhân viên ---
                    decimal grandTotal = 0;
                    foreach (var employeeGroup in groupedData)
                    {
                        // Dòng tên nhân viên
                        var empNameCell = worksheet.Cell(currentRow, 1);
                        empNameCell.Value = $"Nhân viên: {employeeGroup.EmployeeName}";
                        empNameCell.Style.Font.Bold = true;
                        empNameCell.Style.Font.FontSize = 12;
                        worksheet.Range(currentRow, 1, currentRow, 9).Merge().Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7"); // Màu xanh nhạt hơn
                        worksheet.Range(currentRow, 1, currentRow, 9).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                        currentRow++;

                        // Các ca làm của nhân viên
                        int stt = 1;
                        foreach (var assignment in employeeGroup.Assignments)
                        {
                            worksheet.Cell(currentRow, 1).Value = stt++;
                            worksheet.Cell(currentRow, 2).Value = assignment.WorkDate; // DateTime
                            worksheet.Cell(currentRow, 3).Value = assignment.Shift != null ? $"{assignment.Shift.StartTime:hh\\:mm}-{assignment.Shift.EndTime:hh\\:mm}" : "N/A";

                            var clockInCell = worksheet.Cell(currentRow, 4);
                            if (assignment.ActualClockIn.HasValue) clockInCell.Value = assignment.ActualClockIn.Value; else clockInCell.Value = "-";
                            clockInCell.Style.DateFormat.Format = "HH:mm";

                            var clockOutCell = worksheet.Cell(currentRow, 5);
                            if (assignment.ActualClockOut.HasValue) clockOutCell.Value = assignment.ActualClockOut.Value; else clockOutCell.Value = "-";
                            clockOutCell.Style.DateFormat.Format = "HH:mm";

                            var workedHoursCell = worksheet.Cell(currentRow, 6);
                            if (assignment.WorkedHours.HasValue) workedHoursCell.Value = assignment.WorkedHours.Value; else workedHoursCell.Value = "-";
                            workedHoursCell.Style.NumberFormat.Format = "#,##0.00";

                            var wageRateCell = worksheet.Cell(currentRow, 7);
                            if (assignment.WageRateSnapshot.HasValue) wageRateCell.Value = assignment.WageRateSnapshot.Value; else wageRateCell.Value = "-";
                            wageRateCell.Style.NumberFormat.Format = "#,##0";

                            var finalWageCell = worksheet.Cell(currentRow, 8);
                            if (assignment.FinalWage.HasValue) finalWageCell.Value = assignment.FinalWage.Value; else finalWageCell.Value = "-";
                            finalWageCell.Style.NumberFormat.Format = "#,##0";

                            worksheet.Cell(currentRow, 9).Value = assignment.ApprovalNote;

                            worksheet.Range(currentRow, 1, currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            worksheet.Cell(currentRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            worksheet.Range(currentRow, 7, currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            worksheet.Range(currentRow, 1, currentRow, 9).Style.Border.SetInsideBorder(XLBorderStyleValues.Dotted);
                            worksheet.Range(currentRow, 1, currentRow, 9).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

                            currentRow++;
                        }

                        // Dòng tổng cộng của nhân viên
                        var totalLabelCell = worksheet.Cell(currentRow, 7);
                        totalLabelCell.Value = "Tổng cộng:";
                        totalLabelCell.Style.Font.Bold = true;
                        totalLabelCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        var totalValueCell = worksheet.Cell(currentRow, 8);
                        totalValueCell.Value = employeeGroup.TotalWage;
                        totalValueCell.Style.NumberFormat.Format = "#,##0";
                        totalValueCell.Style.Font.Bold = true;
                        totalValueCell.Style.Fill.BackgroundColor = XLColor.Yellow;

                        worksheet.Range(currentRow, 1, currentRow, 9).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                        worksheet.Range(currentRow, 1, currentRow, 9).Style.Border.SetTopBorder(XLBorderStyleValues.Thin);

                        grandTotal += employeeGroup.TotalWage;
                        currentRow++;
                        currentRow++;
                    }

                    // --- Dòng tổng cộng cuối cùng ---
                    worksheet.Cell(currentRow, 7).Value = "TỔNG CHI TẤT CẢ:";
                    worksheet.Cell(currentRow, 7).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 7).Style.Font.FontSize = 13;
                    worksheet.Cell(currentRow, 7).Style.Font.FontColor = XLColor.Red;
                    worksheet.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    worksheet.Cell(currentRow, 8).Value = grandTotal;
                    worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0";
                    worksheet.Cell(currentRow, 8).Style.Font.Bold = true;
                    worksheet.Cell(currentRow, 8).Style.Font.FontSize = 13;
                    worksheet.Cell(currentRow, 8).Style.Font.FontColor = XLColor.Red;


                    // --- Điều chỉnh độ rộng cột và Lưu file (Giữ nguyên) ---
                    worksheet.Columns().AdjustToContents();
                    worksheet.Column(2).Width = 15;
                    worksheet.Column(3).Width = 18;
                    //worksheet.Column(7).Width = 18; // Bỏ comment nếu muốn
                    worksheet.Column(8).Width = 20;
                    worksheet.Column(9).Width = 35;
                    worksheet.Column(7).Width = 25;


                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();
                        string fileName = $"BaoCaoLuong_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.xlsx";
                        _logger.LogInformation("Xuất Excel thành công: {FileName}", fileName);
                        return File(
                            content,
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra khi xuất Excel báo cáo lương.");
                // Trả về lỗi 500 với thông báo JSON
                return StatusCode(500, new { success = false, message = "Đã xảy ra lỗi máy chủ khi tạo file Excel. Vui lòng thử lại." });
            }
        }

        // --- Các hàm tiện ích (Helper Functions) ---
        private DateTime? CombineDateTime(string? dateStr, string? timeStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr) || string.IsNullOrWhiteSpace(timeStr)) { return null; }
            if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var datePart) &&
                TimeSpan.TryParseExact(timeStr, @"hh\:mm", CultureInfo.InvariantCulture, out var timePart))
            { return datePart.Add(timePart); }
            _logger.LogWarning("Không thể parse CombineDateTime với date='{DateStr}' và time='{TimeStr}'", dateStr, timeStr);
            return null;
        }

        private decimal? CalculateWorkedHours(DateTime? clockIn, DateTime? clockOut)
        {
            if (clockIn.HasValue && clockOut.HasValue && clockOut > clockIn)
            { TimeSpan duration = clockOut.Value - clockIn.Value; return (decimal)Math.Round(duration.TotalHours, 2); }
            return null;
        }
    }
}