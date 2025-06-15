using Coffee_Shop_Management.Hubs;
using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Route("api/table-request")]
    [ApiController]
    public class TableRequestController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly ILogger<TableRequestController> _logger;

        public TableRequestController(AppDbContext context, IHubContext<NotificationHub> hubContext, ILogger<TableRequestController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        public class ServiceRequest
        {
            public int TableId { get; set; }
            public string RequestType { get; set; } // "Order" hoặc "Payment"
        }

        [HttpPost("service")]
        public async Task<IActionResult> RequestService([FromBody] ServiceRequest request)
        {
            // <<-- LOG 1: Ghi lại yêu cầu thô nhận được
            _logger.LogInformation("Nhận được yêu cầu từ thiết bị: TableId={TableId}, RequestType='{RequestType}'", request?.TableId, request?.RequestType);

            if (request == null || request.TableId <= 0 || string.IsNullOrEmpty(request.RequestType))
            {
                // <<-- LOG 2: Ghi lại lỗi nếu yêu cầu không hợp lệ
                _logger.LogWarning("Yêu cầu không hợp lệ. Dữ liệu bị thiếu hoặc sai.");
                return BadRequest(new { success = false, message = "Yêu cầu không hợp lệ." });
            }

            var table = await _context.Tables.FirstOrDefaultAsync(t => t.Id == request.TableId);
            if (table == null)
            {
                // <<-- LOG 3: Ghi lại lỗi nếu không tìm thấy bàn
                _logger.LogWarning("Không tìm thấy bàn với ID = {TableId} trong CSDL.", request.TableId);
                return NotFound(new { success = false, message = $"Không tìm thấy bàn với ID {request.TableId}." });
            }

            // <<-- LOG 4: Ghi lại trạng thái hiện tại của bàn trước khi thay đổi
            _logger.LogInformation("Tìm thấy bàn '{NameTable}'. Trạng thái Request hiện tại: {CurrentRequest}", table.NameTable, table.Request);

            int newRequestStatus = 0;
            switch (request.RequestType.ToLower())
            {
                case "order":
                    newRequestStatus = 1; // 1 = Gọi món
                    break;
                case "payment":
                    newRequestStatus = 2; // 2 = Thanh toán
                    break;
                default:
                    // <<-- LOG 5: Ghi lại lỗi nếu loại yêu cầu không được hỗ trợ
                    _logger.LogWarning("Loại yêu cầu '{RequestType}' không được hỗ trợ.", request.RequestType);
                    return BadRequest(new { success = false, message = "Loại yêu cầu không được hỗ trợ." });
            }

            table.Request = newRequestStatus;
            _context.Tables.Update(table);
            await _context.SaveChangesAsync();

            // <<-- LOG 6: Ghi lại trạng thái mới sau khi cập nhật thành công
            _logger.LogInformation("Đã cập nhật CSDL cho bàn '{NameTable}'. Trạng thái Request mới: {NewRequest}", table.NameTable, table.Request);

            string message = $"Bàn {table.NameTable} yêu cầu THANH TOÁN!";
            if (request.RequestType.Equals("Order", StringComparison.OrdinalIgnoreCase))
            {
                message = $"Bàn {table.NameTable} muốn GỌI MÓN!";
            }

            // <<-- LOG 7: Ghi lại thông báo sắp được gửi qua SignalR
            _logger.LogInformation("Chuẩn bị phát thông báo qua SignalR: '{Message}'", message);
            await _hubContext.Clients.All.SendAsync("ReceiveTableRequest", new
            {
                tableId = table.Id,
                tableName = table.NameTable,
                requestType = request.RequestType,
                tableRequestStatus = table.Request,
                message = message
            });

            // <<-- LOG 8: Ghi lại hành động trả kết quả về cho thiết bị
            _logger.LogInformation("Gửi phản hồi HTTP 200 OK về cho thiết bị.");
            return Ok(new { success = true, message = "Yêu cầu đã được ghi nhận." });
        }
    }
}