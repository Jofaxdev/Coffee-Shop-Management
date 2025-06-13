using ClosedXML.Excel;
using Coffee_Shop_Management.Areas.Admin.ViewModels;
using Coffee_Shop_Management.Models; // Đảm bảo namespace này chứa ShiftEditVM
using Microsoft.AspNetCore.Authorization; // Bỏ comment nếu bạn dùng phân quyền
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic; // Cần cho List<>
using System.Globalization;
using System.IO; // Cần cho MemoryStream
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using static Coffee_Shop_Management.Models.AppDbContext;

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    // [Authorize(Roles = WebsiteRoles.WebsiteAdmin + "," + WebsiteRoles.WebsiteManager)] // Ví dụ phân quyền
    public class EmployeeAssignmentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        // private readonly ILogger<EmployeeAssignmentController> _logger; // Thêm ILogger nếu bạn muốn logging chuyên nghiệp hơn

        public EmployeeAssignmentController(AppDbContext context, UserManager<AppUser> userManager /*, ILogger<EmployeeAssignmentController> logger*/)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            // _logger = logger;
        }

        // GET: Admin/EmployeeAssignment
        // Hiển thị view chính, dữ liệu sẽ được load bằng AJAX
        public IActionResult Index()
        {
            return View();
        }

        // GET: Admin/EmployeeAssignment/ExportWeekScheduleToExcel
        [HttpGet]
        public async Task<IActionResult> ExportWeekScheduleToExcel(string startDate)
        {
            if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var weekStartDate))
            {
                return Json(new { success = false, message = "Định dạng ngày bắt đầu không hợp lệ (cần yyyy-MM-dd)." });
            }

            var weekEndDate = weekStartDate.AddDays(6);
            var vietnameseCulture = new CultureInfo("vi-VN");
            var datesOfWeek = Enumerable.Range(0, 7).Select(i => weekStartDate.AddDays(i)).ToList();

            try
            {
                var shifts = await _context.Shifts
                                     .OrderBy(s => s.StartTime)
                                     .ToListAsync();
                if (!shifts.Any())
                {
                    return Json(new { success = false, message = "Không tìm thấy thông tin ca làm việc." });
                }

                var assignments = await _context.EmployeeAssignments
                    .Include(a => a.Employee) // Cần Include Employee để lấy EmployeeId
                    .Where(a => a.WorkDate >= weekStartDate && a.WorkDate <= weekEndDate && a.EmployeeId != null)
                    .Select(a => new { a.EmployeeId, a.WorkDate, a.ShiftId })
                    .ToListAsync();

                var assignmentLookup = assignments
                    .Select(a => (EmployeeId: a.EmployeeId, WorkDate: a.WorkDate.Date, ShiftId: a.ShiftId))
                    .ToHashSet();

                var employeeIdsInWeek = assignments.Select(a => a.EmployeeId).Distinct().ToList();
                var employeesInWeek = await _context.Users
                                              .Where(u => employeeIdsInWeek.Contains(u.Id))
                                              .OrderBy(u => u.Name)
                                              .Select(u => new { u.Id, u.Name })
                                              .ToListAsync();

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add($"Lịch tuần {weekStartDate:dd.MM} - {weekEndDate:dd.MM}");

                    int titleRow = 1;
                    int headerRowDay = 2;
                    int headerRowDate = 3;
                    int headerRowShift = 4;
                    int startDataRow = headerRowShift + 1;
                    int startDataCol = 2;
                    int totalShiftColumns = shifts.Count;
                    int totalColumns = 1 + datesOfWeek.Count * totalShiftColumns;

                    var titleCell = worksheet.Cell(titleRow, 1);
                    titleCell.Value = $"BẢNG PHÂN CÔNG LỊCH LÀM VIỆC TUẦN ({weekStartDate:dd/MM/yyyy} - {weekEndDate:dd/MM/yyyy})";
                    worksheet.Range(titleRow, 1, titleRow, totalColumns).Merge();
                    titleCell.Style.Font.Bold = true;
                    titleCell.Style.Font.FontSize = 14;
                    titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    titleCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                    var empHeaderCell = worksheet.Cell(headerRowDay, 1);
                    empHeaderCell.Value = "Nhân viên";
                    worksheet.Range(headerRowDay, 1, headerRowShift, 1).Merge();
                    empHeaderCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    empHeaderCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    empHeaderCell.Style.Font.Bold = true;
                    empHeaderCell.Style.Fill.BackgroundColor = XLColor.LightGray;

                    for (int i = 0; i < datesOfWeek.Count; i++)
                    {
                        var currentDate = datesOfWeek[i];
                        int dayStartCol = startDataCol + (i * totalShiftColumns);
                        int dayEndCol = dayStartCol + totalShiftColumns - 1;

                        var dayCell = worksheet.Cell(headerRowDay, dayStartCol);
                        dayCell.Value = vietnameseCulture.DateTimeFormat.GetDayName(currentDate.DayOfWeek);
                        worksheet.Range(headerRowDay, dayStartCol, headerRowDay, dayEndCol).Merge();
                        dayCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        dayCell.Style.Font.Bold = true;
                        dayCell.Style.Fill.BackgroundColor = XLColor.LightGray;

                        var dateCell = worksheet.Cell(headerRowDate, dayStartCol);
                        dateCell.Value = currentDate.ToString("dd/MM");
                        worksheet.Range(headerRowDate, dayStartCol, headerRowDate, dayEndCol).Merge();
                        dateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        dateCell.Style.Font.Italic = true;
                        dateCell.Style.Fill.BackgroundColor = XLColor.LightGray;

                        for (int j = 0; j < totalShiftColumns; j++)
                        {
                            var shiftCell = worksheet.Cell(headerRowShift, dayStartCol + j);
                            shiftCell.Value = shifts[j].Name;
                            shiftCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            shiftCell.Style.Font.Bold = true;
                            shiftCell.Style.Fill.BackgroundColor = XLColor.LightGray;
                        }
                    }
                    var headerRange = worksheet.Range(headerRowDay, 1, headerRowShift, totalColumns);
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                    int currentRow = startDataRow;
                    if (!employeesInWeek.Any())
                    {
                        worksheet.Cell(currentRow, 1).Value = "Không có nhân viên nào được phân công trong tuần này.";
                        worksheet.Range(currentRow, 1, currentRow, totalColumns).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        currentRow++;
                    }
                    else
                    {
                        foreach (var employee in employeesInWeek)
                        {
                            worksheet.Cell(currentRow, 1).Value = employee.Name ?? "N/A";
                            for (int i = 0; i < datesOfWeek.Count; i++)
                            {
                                var currentDate = datesOfWeek[i].Date;
                                for (int j = 0; j < totalShiftColumns; j++)
                                {
                                    var currentShift = shifts[j];
                                    int currentCol = startDataCol + (i * totalShiftColumns) + j;
                                    var currentCell = worksheet.Cell(currentRow, currentCol);
                                    if (assignmentLookup.Contains((employee.Id, currentDate, currentShift.ShiftId)))
                                    {
                                        currentCell.Value = "X";
                                        currentCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                    }
                                    else { currentCell.Value = ""; }
                                    currentCell.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                                }
                            }
                            var dataRowRange = worksheet.Range(currentRow, 1, currentRow, totalColumns);
                            dataRowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            dataRowRange.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
                            dataRowRange.Style.Border.RightBorder = XLBorderStyleValues.Thin;
                            currentRow++;
                        }
                    }

                    int notesStartRow = currentRow + 1;
                    var noteTitleCell = worksheet.Cell(notesStartRow, 1);
                    noteTitleCell.Value = "Ghi chú thời gian ca làm việc:";
                    worksheet.Range(notesStartRow, 1, notesStartRow, 3).Merge();
                    noteTitleCell.Style.Font.Bold = true;
                    noteTitleCell.Style.Font.Italic = true;

                    for (int k = 0; k < shifts.Count; k++)
                    {
                        var shift = shifts[k];
                        var noteCell = worksheet.Cell(notesStartRow + 1 + k, 1);
                        noteCell.Value = $" - {shift.Name}: {shift.StartTime:hh\\:mm} - {shift.EndTime:hh\\:mm}";
                        noteCell.Style.Font.Italic = true;
                    }

                    worksheet.Column(1).AdjustToContents();
                    for (int i = 0; i < datesOfWeek.Count * totalShiftColumns; i++)
                    {
                        worksheet.Column(startDataCol + i).Width = 7;
                    }

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();
                        string fileName = $"LichLamViecChiTiet_Tuan_{weekStartDate:yyyy-MM-dd}.xlsx";
                        string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                        return File(content, contentType, fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting Excel (Detailed Grid Format with Title/Notes): {ex.ToString()}");
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi tạo file Excel chi tiết." });
            }
        }

        // --- API ACTIONS CHO LỊCH LÀM VIỆC ---

        /// <summary>
        /// API để lấy danh sách phân công cho một tuần cụ thể.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAssignments(string startDate)
        {
            if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var weekStartDate))
            {
                return BadRequest(new { success = false, message = "Định dạng ngày bắt đầu không hợp lệ (cần yyyy-MM-dd)." });
            }
            var weekEndDate = weekStartDate.AddDays(6);
            try
            {
                var assignments = await _context.EmployeeAssignments
                    .AsNoTracking()
                    .Include(a => a.Employee)
                    .Where(a => a.WorkDate >= weekStartDate && a.WorkDate <= weekEndDate)
                    .Select(a => new
                    {
                        assignmentId = a.AssignmentId,
                        employeeId = a.EmployeeId,
                        employeeName = a.Employee != null ? a.Employee.Name : "N/A",
                        employeeAvatar = a.Employee != null ? a.Employee.ImageUrl : null,
                        workDate = a.WorkDate.ToString("yyyy-MM-dd"),
                        shiftId = a.ShiftId,
                    })
                    .ToListAsync();
                return Json(assignments);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAssignments: {ex.ToString()}");
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi tải dữ liệu phân công." });
            }
        }

        /// <summary>
        /// API để lấy danh sách nhân viên (có thể tìm kiếm) cho modal lựa chọn.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetEmployees(string? searchTerm)
        {
            try
            {
                var query = _context.Users
                    .AsNoTracking()
                    .Where(u => !u.DeleteTemp && !u.LockAccount);

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    string lowerSearchTerm = searchTerm.ToLower().Trim();
                    query = query.Where(u => (u.Name != null && u.Name.ToLower().Contains(lowerSearchTerm)) ||
                                             (u.Email != null && u.Email.ToLower().Contains(lowerSearchTerm)) ||
                                             (u.PhoneNumber != null && u.PhoneNumber.Contains(lowerSearchTerm)));
                }

                var employees = await query
                    .OrderBy(u => u.Name)
                    .Select(u => new {
                        id = u.Id,
                        name = u.Name ?? u.UserName ?? "N/A",
                        avatarUrl = u.ImageUrl
                    })
                    .Take(30)
                    .ToListAsync();
                return Json(employees);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEmployees: {ex.ToString()}");
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi tải danh sách nhân viên." });
            }
        }

        /// <summary>
        /// API để thêm một phân công mới.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddAssignment(string employeeId, int shiftId, string workDateStr)
        {
            if (string.IsNullOrEmpty(employeeId) || shiftId <= 0 ||
                !DateTime.TryParseExact(workDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var workDate))
            {
                return BadRequest(new { success = false, message = "Dữ liệu đầu vào không hợp lệ." });
            }
            try
            {
                var shift = await _context.Shifts.FindAsync(shiftId);
                var employee = await _userManager.FindByIdAsync(employeeId);
                var assignerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (shift == null) return NotFound(new { success = false, message = "Không tìm thấy ca làm việc đã chọn." });
                if (employee == null) return NotFound(new { success = false, message = "Không tìm thấy nhân viên đã chọn." });
                if (assignerUserId == null) return Unauthorized(new { success = false, message = "Không thể xác thực người thực hiện." });

                bool existing = await _context.EmployeeAssignments
                   .AnyAsync(a => a.EmployeeId == employeeId && a.ShiftId == shiftId && a.WorkDate == workDate);

                if (existing)
                {
                    return BadRequest(new { success = false, message = $"Nhân viên '{employee.Name ?? employeeId}' đã được phân công ca '{shift.Name}' vào ngày {workDate:dd/MM/yyyy}." });
                }

                var newAssignment = new EmployeeAssignment
                {
                    EmployeeId = employeeId,
                    ShiftId = shiftId,
                    WorkDate = workDate,
                    AssignedStartTime = shift.StartTime,
                    AssignedEndTime = shift.EndTime,
                    WageRateSnapshot = employee.WageRate ?? 0,
                    AssignedByUserId = assignerUserId,
                    AssignmentCreatedAt = DateTime.UtcNow,
                    AttendanceStatus = "Chưa vào ca",
                    ApprovalStatus = "Chờ duyệt",
                };

                _context.EmployeeAssignments.Add(newAssignment);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = "Phân công thành công!",
                    assignment = new
                    {
                        assignmentId = newAssignment.AssignmentId,
                        employeeId = newAssignment.EmployeeId,
                        employeeName = employee.Name ?? "N/A",
                        employeeAvatar = employee.ImageUrl,
                        workDate = newAssignment.WorkDate.ToString("yyyy-MM-dd"),
                        shiftId = newAssignment.ShiftId
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddAssignment: {ex.ToString()}");
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi thêm phân công." });
            }
        }

        /// <summary>
        /// API để xóa một phân công.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveAssignment(int assignmentId)
        {
            if (assignmentId <= 0)
            {
                return BadRequest(new { success = false, message = "ID phân công không hợp lệ." });
            }
            try
            {
                var assignment = await _context.EmployeeAssignments.FindAsync(assignmentId);
                if (assignment == null)
                {
                    return NotFound(new { success = false, message = "Không tìm thấy phân công để xóa." });
                }

                _context.EmployeeAssignments.Remove(assignment);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã xóa phân công." });
            }
            catch (DbUpdateException dbEx)
            {
                Console.WriteLine($"DB error in RemoveAssignment: {dbEx.ToString()}");
                return StatusCode(500, new { success = false, message = "Lỗi CSDL khi xóa. Có thể có dữ liệu liên quan." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RemoveAssignment: {ex.ToString()}");
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi xóa phân công." });
            }
        }

        /// <summary>
        /// API để di chuyển một phân công sang ngày/ca khác.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveAssignment(int assignmentId, int targetShiftId, string targetWorkDateStr)
        {
            if (assignmentId <= 0 || targetShiftId <= 0 ||
                !DateTime.TryParseExact(targetWorkDateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var targetWorkDate))
            {
                return BadRequest(new { success = false, message = "Dữ liệu di chuyển không hợp lệ." });
            }
            try
            {
                var assignment = await _context.EmployeeAssignments
                                          .Include(a => a.Employee)
                                          .FirstOrDefaultAsync(a => a.AssignmentId == assignmentId);
                var targetShift = await _context.Shifts.FindAsync(targetShiftId);

                if (assignment == null) return NotFound(new { success = false, message = "Không tìm thấy phân công gốc." });
                if (targetShift == null) return NotFound(new { success = false, message = "Không tìm thấy ca làm việc đích." });
                if (assignment.Employee == null) return NotFound(new { success = false, message = "Không tìm thấy nhân viên của phân công." });

                bool existing = await _context.EmployeeAssignments
                   .AnyAsync(a => a.AssignmentId != assignmentId &&
                                 a.EmployeeId == assignment.EmployeeId &&
                                 a.ShiftId == targetShiftId &&
                                 a.WorkDate == targetWorkDate);

                if (existing)
                {
                    return BadRequest(new { success = false, message = $"Nhân viên '{assignment.Employee.Name}' đã được phân công ca '{targetShift.Name}' vào ngày {targetWorkDate:dd/MM/yyyy}." });
                }

                assignment.WorkDate = targetWorkDate;
                assignment.ShiftId = targetShiftId;
                assignment.AssignedStartTime = targetShift.StartTime;
                assignment.AssignedEndTime = targetShift.EndTime;

                _context.EmployeeAssignments.Update(assignment);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã di chuyển phân công." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in MoveAssignment: {ex.ToString()}");
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi di chuyển phân công." });
            }
        }


        // --- API ACTIONS CHO CHỈNH SỬA CA LÀM VIỆC ---

        /// <summary>
        /// API để lấy thông tin các ca làm việc cho modal chỉnh sửa.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetShiftsForEditing()
        {
            try
            {
                var shifts = await _context.Shifts
                                     .OrderBy(s => s.StartTime)
                                     .Select(s => new
                                     {
                                         s.ShiftId,
                                         s.Name,
                                         StartTime = s.StartTime.ToString(@"hh\:mm"),
                                         EndTime = s.EndTime.ToString(@"hh\:mm")
                                     })
                                     .ToListAsync();
                return Json(shifts);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting shifts for editing: {ex}");
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi tải thông tin ca." });
            }
        }

        /// <summary>
        /// API để cập nhật thời gian bắt đầu/kết thúc cho các ca làm việc.
        /// </summary>
        /// <param name="updatedShifts">Danh sách thông tin ca cần cập nhật.</param>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateShiftTimes([FromBody] List<ShiftEditVM> updatedShifts)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors)
                                              .Select(e => e.ErrorMessage)
                                              .ToList();
                return BadRequest(new { success = false, message = "Dữ liệu không hợp lệ.", errors = errors });
            }

            if (updatedShifts == null || !updatedShifts.Any())
            {
                return BadRequest(new { success = false, message = "Không có dữ liệu ca nào được gửi lên." });
            }

            var errorsProcessing = new List<string>();
            bool hasChanges = false;

            try
            {
                var shiftIdsToUpdate = updatedShifts.Select(s => s.ShiftId).ToList();
                var shiftsFromDb = await _context.Shifts
                                             .Where(s => shiftIdsToUpdate.Contains(s.ShiftId))
                                             .ToListAsync();
                var shiftsDbDict = shiftsFromDb.ToDictionary(s => s.ShiftId);

                foreach (var updatedShift in updatedShifts)
                {
                    if (shiftsDbDict.TryGetValue(updatedShift.ShiftId, out var shiftToUpdate))
                    {
                        if (TimeSpan.TryParseExact(updatedShift.StartTime, @"hh\:mm", CultureInfo.InvariantCulture, out var newStartTime) &&
                            TimeSpan.TryParseExact(updatedShift.EndTime, @"hh\:mm", CultureInfo.InvariantCulture, out var newEndTime))
                        {
                            // Optional: Add validation logic if needed (e.g., StartTime < EndTime)
                            // if (newStartTime >= newEndTime) {
                            //     errorsProcessing.Add($"Ca '{shiftToUpdate.Name}': Giờ kết thúc phải sau giờ bắt đầu.");
                            //     continue;
                            // }

                            if (shiftToUpdate.StartTime != newStartTime || shiftToUpdate.EndTime != newEndTime)
                            {
                                shiftToUpdate.StartTime = newStartTime;
                                shiftToUpdate.EndTime = newEndTime;
                                hasChanges = true;
                            }
                        }
                        else
                        {
                            errorsProcessing.Add($"Không thể phân tích định dạng giờ cho ca ID {updatedShift.ShiftId}. Định dạng yêu cầu là HH:mm.");
                        }
                    }
                    else
                    {
                        errorsProcessing.Add($"Không tìm thấy ca làm việc với ID {updatedShift.ShiftId}.");
                    }
                }

                if (errorsProcessing.Any())
                {
                    return BadRequest(new { success = false, message = "Có lỗi xảy ra trong quá trình xử lý:", errors = errorsProcessing });
                }

                if (hasChanges)
                {
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Cập nhật giờ làm việc thành công!" });
                }
                else
                {
                    return Json(new { success = true, message = "Không có thay đổi nào được thực hiện." });
                }
            }
            catch (DbUpdateException dbEx)
            {
                Console.WriteLine($"Database error updating shifts: {dbEx}");
                return StatusCode(500, new { success = false, message = "Lỗi cơ sở dữ liệu khi cập nhật ca làm việc." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating shifts: {ex}");
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ không mong muốn khi cập nhật ca." });
            }
        }

    } // End of Controller Class
}