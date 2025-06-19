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

        var notificationData = new
        {
            notificationId = notification.Id,
            tableCode = table.TableCode,
            tableName = table.NameTable,
            requestType = payload.RequestType,
            requestStatus = table.Request,
            message = message,
            createdAt = notification.CreatedAt,
            isRead = false,
            acknowledgedByUserName = (string)null
        };

        await _hubContext.Clients.All.SendAsync("ReceiveTableRequest", notificationData);
        return Ok(new { message = $"Đã ghi nhận yêu cầu '{payload.RequestType}' cho bàn '{table.NameTable}' thành công." });
    }

    // === API MỚI ĐỂ XÁC NHẬN MỘT THÔNG BÁO CỤ THỂ ===
    [Authorize] // Đảm bảo chỉ người dùng đăng nhập mới có thể xác nhận
    [HttpPost("AcknowledgeNotification/{notificationId}")]
    public async Task<IActionResult> AcknowledgeSingleNotification(int notificationId)
    {
        var notification = await _context.Notifications.FindAsync(notificationId);
        if (notification == null) return NotFound();
        if (notification.IsRead) return Ok(); // Đã có người khác xác nhận

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = User.Identity.Name;

        notification.IsRead = true;
        notification.ReadAt = DateTime.Now;
        notification.AcknowledgedByUserId = userId;
        notification.AcknowledgedByUserName = userName;

        // Reset trạng thái yêu cầu của bàn tương ứng
        var table = await _context.Tables.FirstOrDefaultAsync(t => t.TableCode == notification.TableCode);
        if (table != null)
        {
            table.Request = 0;
        }

        await _context.SaveChangesAsync();

        // Gửi thông báo tới các client
        await _hubContext.Clients.All.SendAsync("ReceiveTableRequest", new { tableCode = notification.TableCode, requestStatus = 0 });
        await _hubContext.Clients.All.SendAsync("NotificationAcknowledged", new
        {
            NotificationId = notification.Id,
            AcknowledgedBy = userName,
            ReadAt = notification.ReadAt
        });

        return Ok();
    }

    [HttpGet("GetRecentNotifications")]
    public async Task<IActionResult> GetRecentNotifications()
    {
        var notifications = await _context.Notifications
            .OrderByDescending(n => n.CreatedAt)
            .Take(30) // Lấy 30 thông báo gần nhất
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