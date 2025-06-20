using Coffee_Shop_Management.Models;
using Coffee_Shop_Management.Utilites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = WebsiteRoles.WebsiteUser)]
    public class AttendanceScannerController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _lateGracePeriod = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _earlyLeaveTolerance = TimeSpan.FromMinutes(5);

        public AttendanceScannerController(AppDbContext context, UserManager<AppUser> userManager, IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                ViewData["UserName"] = user.Name ?? user.UserName;
                ViewData["UserAvatar"] = user.ImageUrl ?? "/images/avatar/default-avatar.png";
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> RecordAttendance([FromBody] QrScanModel model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.QrData))
            {
                return BadRequest(new { success = false, message = "Không nhận được dữ liệu QR." });
            }

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new { success = false, message = "Không thể xác thực người dùng. Vui lòng đăng nhập lại." });
                }

                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);

                // =================================================================
                // === BẮT ĐẦU VÙNG MÃ ĐÃ SỬA ĐỔI (XÁC THỰC NGHIÊM NGẶT) ===
                // =================================================================
                var principal = tokenHandler.ValidateToken(model.QrData, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _configuration["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = _configuration["Jwt:Audience"],
                    ValidateLifetime = true,
                    // Đặt ClockSkew = 0 để không có thời gian du di. Hết 1 phút là hết hạn.
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);
                // =================================================================
                // === KẾT THÚC VÙNG MÃ ĐÃ SỬA ĐỔI ===
                // =================================================================

                var jwtToken = (JwtSecurityToken)validatedToken;
                var shiftId = int.Parse(jwtToken.Claims.First(x => x.Type == "shiftId").Value);
                var date = DateTime.ParseExact(jwtToken.Claims.First(x => x.Type == "date").Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var type = jwtToken.Claims.FirstOrDefault(x => x.Type == "type")?.Value;

                if (string.IsNullOrEmpty(type))
                {
                    return BadRequest(new { success = false, message = "Mã QR không hợp lệ (thiếu loại hành động)." });
                }

                var assignment = await _context.EmployeeAssignments
                    .FirstOrDefaultAsync(a => a.EmployeeId == userId && a.ShiftId == shiftId && a.WorkDate.Date == date.Date);

                if (assignment == null)
                {
                    return BadRequest(new { success = false, message = $"Bạn không được phân công trong ca làm việc này vào ngày {date:dd/MM/yyyy}." });
                }

                var now = DateTime.Now;
                var scheduledStart = assignment.WorkDate.Date.Add(assignment.AssignedStartTime);
                var scheduledEnd = assignment.WorkDate.Date.Add(assignment.AssignedEndTime);

                if (type == "in")
                {
                    if (assignment.ActualClockIn != null)
                    {
                        return BadRequest(new { success = false, message = "Bạn đã vào ca này rồi." });
                    }

                    assignment.ActualClockIn = now;

                    if (now > scheduledStart.Add(_lateGracePeriod))
                    {
                        assignment.AttendanceStatus = "Vào trễ";
                    }
                    else
                    {
                        assignment.AttendanceStatus = "Đúng giờ";
                    }

                    _context.EmployeeAssignments.Update(assignment);
                    await _context.SaveChangesAsync();
                    return Ok(new { success = true, message = $"Check-in thành công lúc {now:HH:mm:ss}", status = assignment.AttendanceStatus });
                }
                else if (type == "out")
                {
                    if (assignment.ActualClockIn == null)
                    {
                        return BadRequest(new { success = false, message = "Bạn chưa vào ca, không thể kết ca." });
                    }
                    if (assignment.ActualClockOut != null)
                    {
                        return BadRequest(new { success = false, message = "Bạn đã kết ca này rồi." });
                    }

                    assignment.ActualClockOut = now;

                    bool isLate = assignment.ActualClockIn.Value > scheduledStart.Add(_lateGracePeriod);
                    bool isEarly = now < scheduledEnd.Subtract(_earlyLeaveTolerance);

                    if (isLate && isEarly) { assignment.AttendanceStatus = "Vào trễ, Ra sớm"; }
                    else if (isLate) { assignment.AttendanceStatus = "Vào trễ"; }
                    else if (isEarly) { assignment.AttendanceStatus = "Ra sớm"; }
                    else
                    {
                        if (assignment.AttendanceStatus != "Vào trễ")
                        {
                            assignment.AttendanceStatus = "Đúng giờ";
                        }
                    }

                    _context.EmployeeAssignments.Update(assignment);
                    await _context.SaveChangesAsync();
                    return Ok(new { success = true, message = $"Check-out thành công lúc {now:HH:mm:ss}", status = assignment.AttendanceStatus });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Loại mã QR không được hỗ trợ." });
                }
            }
            catch (SecurityTokenExpiredException)
            {
                return BadRequest(new { success = false, message = "MÃ ĐÃ HẾT HẠN. Vui lòng yêu cầu mã QR mới." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error recording attendance: {ex}");
                return StatusCode(500, new { success = false, message = "Mã QR không hợp lệ hoặc đã có lỗi xảy ra." });
            }
        }
    }

    public class QrScanModel
    {
        public string QrData { get; set; }
    }
}