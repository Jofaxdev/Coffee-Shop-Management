using Coffee_Shop_Management.Hubs;
using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using static Coffee_Shop_Management.Models.AppDbContext;

// DTO để nhận dữ liệu từ ESP8266
public class ServiceRequestPayload
{
    public string TableCode { get; set; }
    public string RequestType { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class TableRequestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IHubContext<AppHub> _hubContext;

    public TableRequestController(AppDbContext context, IHubContext<AppHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    // === HÀM HELPER: GỬI TOÀN BỘ LỊCH SỬ THÔNG BÁO MỚI NHẤT ===
    private async Task BroadcastNotificationHistoryUpdate()
    {
        var notifications = await _context.Notifications
            .OrderByDescending(n => n.CreatedAt)
            .Take(30) // Luôn lấy 30 thông báo gần nhất
            .Select(n => new {
                notificationId = n.Id,
                tableCode = n.TableCode,
                tableName = n.TableName,
                requestType = n.RequestType,
                message = n.Message,
                createdAt = n.CreatedAt,
                isRead = n.IsRead,
                acknowledgedByUserName = n.AcknowledgedByUserName
            })
            .ToListAsync();

        // Gửi sự kiện 'ReceiveNotificationHistoryUpdate' chứa toàn bộ danh sách mới
        await _hubContext.Clients.All.SendAsync("ReceiveNotificationHistoryUpdate", notifications);
    }

    [HttpPost("Service")]
    public async Task<IActionResult> HandleServiceRequest([FromBody] ServiceRequestPayload payload)
    {
        if (payload == null || string.IsNullOrEmpty(payload.TableCode) || string.IsNullOrEmpty(payload.RequestType))
        {
            return BadRequest(new { message = "Dữ liệu không hợp lệ." });
        }

        var table = await _context.Tables.FirstOrDefaultAsync(t => t.TableCode == payload.TableCode);
        if (table == null)
        {
            return NotFound(new { message = $"Không tìm thấy bàn với mã: {payload.TableCode}" });
        }

        int newRequestStatus;
        string message;
        switch (payload.RequestType)
        {
            case "Order":
                newRequestStatus = 1;
                message = $"Bàn '{table.NameTable}' yêu cầu gọi món.";
                break;
            case "Payment":
                newRequestStatus = 2;
                message = $"Bàn '{table.NameTable}' yêu cầu thanh toán.";
                break;
            case "Clear":
                table.Request = 0;
                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("ReceiveTableRequest", new { tableCode = table.TableCode, requestStatus = 0 });
                return Ok(new { message = "Đã xóa yêu cầu." });
            default:
                return BadRequest(new { message = "Loại yêu cầu không được hỗ trợ." });
        }

        table.Request = newRequestStatus;
        table.UpdatedAt = DateTime.Now;

        var notification = new Notification
        {
            TableCode = table.TableCode,
            TableName = table.NameTable,
            RequestType = payload.RequestType,
            Message = message,
            CreatedAt = DateTime.Now,
            IsRead = false
        };
        _context.Notifications.Add(notification);

        await _context.SaveChangesAsync();

        // Cập nhật icon trên bàn
        await _hubContext.Clients.All.SendAsync("ReceiveTableRequest", new { tableCode = table.TableCode, requestStatus = newRequestStatus });

        // === THÊM MỚI: Gửi alert Notyf tức thì cho các client ===
        await _hubContext.Clients.All.SendAsync("ReceiveNewNotificationAlert", message);
        // === KẾT THÚC THÊM MỚI ===

        // Gửi toàn bộ lịch sử mới cho tất cả client
        await BroadcastNotificationHistoryUpdate();

        return Ok(new { message = $"Đã ghi nhận yêu cầu '{payload.RequestType}' cho bàn '{table.NameTable}' thành công." });
    }

    [Authorize]
    [HttpPost("AcknowledgeNotification/{notificationId}")]
    [ValidateAntiForgeryToken] // <-- FIX: Thêm thuộc tính này để yêu cầu token chống giả mạo
    public async Task<IActionResult> AcknowledgeSingleNotification(int notificationId)
    {
        var notification = await _context.Notifications.FindAsync(notificationId);
        if (notification == null || notification.IsRead)
        {
            return Ok();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = User.Identity.Name;

        notification.IsRead = true;
        notification.ReadAt = DateTime.Now;
        notification.AcknowledgedByUserId = userId;
        notification.AcknowledgedByUserName = userName;

        var table = await _context.Tables.FirstOrDefaultAsync(t => t.TableCode == notification.TableCode);
        if (table != null)
        {
            table.Request = 0;
        }

        await _context.SaveChangesAsync();

        // Cập nhật icon trên bàn
        await _hubContext.Clients.All.SendAsync("ReceiveTableRequest", new { tableCode = notification.TableCode, requestStatus = 0 });

        // Gửi toàn bộ lịch sử mới cho tất cả client
        await BroadcastNotificationHistoryUpdate();

        return Ok();
    }

    // API này dùng cho lần tải trang đầu tiên
    [HttpGet("GetRecentNotifications")]
    public async Task<IActionResult> GetRecentNotifications()
    {
        var notifications = await _context.Notifications
            .OrderByDescending(n => n.CreatedAt)
            .Take(30)
            .Select(n => new {
                notificationId = n.Id,
                tableCode = n.TableCode,
                tableName = n.TableName,
                requestType = n.RequestType,
                message = n.Message,
                createdAt = n.CreatedAt,
                isRead = n.IsRead,
                acknowledgedByUserName = n.AcknowledgedByUserName
            })
            .ToListAsync();
        return Ok(notifications);
    }
}