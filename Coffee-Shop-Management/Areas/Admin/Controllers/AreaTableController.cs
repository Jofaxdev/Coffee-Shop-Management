using Coffee_Shop_Management.Hubs;
using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using static Coffee_Shop_Management.Models.AppDbContext;

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/[controller]")]
    public class AreaTableController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<AppHub> _hubContext;

        public AreaTableController(AppDbContext context, IHubContext<AppHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        private (string updaterId, string updaterName) GetCurrentUserInfo()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = User.Identity.Name ?? "Một người dùng";
            return (userId, userName);
        }

        [Route("Index")]
        [Route("")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet("GetAreas")]
        public async Task<IActionResult> GetAreas()
        {
            try
            {
                var areas = await _context.Areas
                    .OrderBy(a => a.DisplayOrder)
                    .ThenBy(a => a.Name)
                    .Select(a => new
                    {
                        areaCode = a.AreaCode,
                        name = a.Name,
                        tableCount = a.Tables.Count(),
                        isActive = a.IsActive,
                        updatedAt = a.UpdatedAt
                    })
                    .ToListAsync();
                return Json(areas);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAreas: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi máy chủ khi tải danh sách khu vực." });
            }
        }

        [HttpGet("GetTablesByArea")]
        public async Task<IActionResult> GetTablesByArea(string areaCode)
        {
            if (string.IsNullOrEmpty(areaCode))
            {
                return BadRequest(new { message = "Mã khu vực không được để trống." });
            }
            try
            {
                var tables = await _context.Tables
                    .Where(t => t.AreaCode == areaCode)
                    .OrderBy(t => t.DisplayOrder)
                    .ThenBy(t => t.NameTable)
                    .Select(t => new
                    {
                        tableCode = t.TableCode,
                        nameTable = t.NameTable,
                        isAvailable = t.IsAvailable,
                        isActive = t.IsActive,
                        updatedAt = t.UpdatedAt,
                        request = t.Request

                    })
                    .ToListAsync();
                return Json(tables);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetTablesByArea: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi máy chủ khi tải danh sách bàn." });
            }
        }

        [HttpGet("GetAreaDetails/{areaCode}")]
        public async Task<IActionResult> GetAreaDetails(string areaCode)
        {
            if (string.IsNullOrEmpty(areaCode))
            {
                return BadRequest(new { message = "Mã khu vực không hợp lệ." });
            }
            var area = await _context.Areas
                .Where(a => a.AreaCode == areaCode)
                .Select(a => new
                {
                    a.AreaCode,
                    a.Name,
                    a.Description,
                    a.IsActive,
                    a.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (area == null)
            {
                return NotFound(new { message = "Không tìm thấy khu vực." });
            }
            return Json(area);
        }

        [HttpGet("GetTableDetails/{tableCode}")]
        public async Task<IActionResult> GetTableDetails(string tableCode)
        {
            if (string.IsNullOrEmpty(tableCode))
            {
                return BadRequest(new { message = "Mã bàn không hợp lệ." });
            }
            var table = await _context.Tables
                .Where(t => t.TableCode == tableCode)
                .Select(t => new
                {
                    t.TableCode,
                    t.NameTable,
                    t.AreaCode,
                    t.IsAvailable,
                    t.IsActive,
                    t.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (table == null)
            {
                return NotFound(new { message = "Không tìm thấy bàn." });
            }
            return Json(table);
        }

        [HttpPost("CreateArea")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateArea([FromForm] Area area, [FromForm] string __ConnectionId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Dữ liệu không hợp lệ.", errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
            }
            try
            {
                bool codeExists = await _context.Areas.AnyAsync(a => a.AreaCode.ToLower() == area.AreaCode.ToLower());
                if (codeExists)
                {
                    return Json(new { success = false, message = "Mã khu vực này đã tồn tại." });
                }
                bool nameExists = await _context.Areas.AnyAsync(a => a.Name.ToLower() == area.Name.ToLower());
                if (nameExists)
                {
                    return Json(new { success = false, message = "Tên khu vực này đã tồn tại." });
                }
                var maxOrder = await _context.Areas.MaxAsync(a => (int?)a.DisplayOrder) ?? 0;
                area.DisplayOrder = maxOrder + 1;
                area.CreatedAt = DateTime.Now;
                area.UpdatedAt = DateTime.Now;

                _context.Add(area);
                await _context.SaveChangesAsync();

                var (updaterId, updaterName) = GetCurrentUserInfo();
                var excludedConnection = GetExcludedConnectionList(__ConnectionId);
                await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveAreaUpdate", new { areaCode = area.AreaCode, tableCount = 0, updaterId, updaterName });

                return Json(new { success = true, message = "Thêm khu vực thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Creating Area: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu dữ liệu vào cơ sở dữ liệu." });
            }
        }

        [HttpPost("EditArea/{areaCode}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditArea(string areaCode, [FromForm] Area area, [FromForm] string __ConnectionId)
        {
            if (areaCode != area.AreaCode)
            {
                return BadRequest(new { message = "Dữ liệu không khớp." });
            }
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Dữ liệu không hợp lệ.", errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
            }
            var existingArea = await _context.Areas.FindAsync(areaCode);
            if (existingArea == null)
            {
                return NotFound(new { message = "Không tìm thấy khu vực để cập nhật." });
            }
            bool nameExists = await _context.Areas.AnyAsync(a => a.AreaCode != areaCode && a.Name.ToLower() == area.Name.ToLower());
            if (nameExists)
            {
                return Json(new { success = false, message = "Tên khu vực này đã được sử dụng." });
            }
            try
            {
                existingArea.Name = area.Name;
                existingArea.Description = area.Description;
                existingArea.IsActive = area.IsActive;
                existingArea.UpdatedAt = DateTime.Now;
                _context.Update(existingArea);
                await _context.SaveChangesAsync();

                var (updaterId, updaterName) = GetCurrentUserInfo();
                var excludedConnection = GetExcludedConnectionList(__ConnectionId);
                await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveAreaUpdate", new { areaCode = existingArea.AreaCode, updaterId, updaterName });

                return Json(new { success = true, message = "Cập nhật khu vực thành công!" });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Json(new { success = false, message = "Dữ liệu đã được sửa đổi bởi người khác. Vui lòng tải lại trang." });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Editing Area: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi cập nhật dữ liệu." });
            }
        }

        [HttpPost("DeleteArea")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteArea([FromForm] string areaCode, [FromForm] string __ConnectionId)
        {
            if (string.IsNullOrEmpty(areaCode))
            {
                return BadRequest(new { message = "Mã khu vực không hợp lệ." });
            }
            var area = await _context.Areas.FindAsync(areaCode);
            if (area == null)
            {
                return Json(new { success = true, message = "Khu vực không tồn tại hoặc đã được xóa." });
            }
            bool hasTables = await _context.Tables.AnyAsync(t => t.AreaCode == areaCode);
            if (hasTables)
            {
                return Json(new { success = false, message = "Không thể xóa khu vực này vì vẫn còn bàn. Vui lòng xóa hoặc chuyển các bàn trước." });
            }
            try
            {
                _context.Areas.Remove(area);
                await _context.SaveChangesAsync();

                var (updaterId, updaterName) = GetCurrentUserInfo();
                var excludedConnection = GetExcludedConnectionList(__ConnectionId);
                await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveAreaUpdate", new { areaCode = area.AreaCode, updaterId, updaterName });

                return Json(new { success = true, message = "Xóa khu vực thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Deleting Area: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi xóa khu vực do ràng buộc dữ liệu." });
            }
        }

        [HttpPost("CreateTable")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTable([FromForm] Table table, [FromForm] string __ConnectionId)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Dữ liệu không hợp lệ.", errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
            }
            try
            {
                bool codeExists = await _context.Tables.AnyAsync(t => t.TableCode.ToLower() == table.TableCode.ToLower());
                if (codeExists)
                {
                    return Json(new { success = false, message = "Mã bàn này đã tồn tại." });
                }
                bool nameExistsInArea = await _context.Tables.AnyAsync(t => t.AreaCode == table.AreaCode && t.NameTable.ToLower() == table.NameTable.ToLower());
                if (nameExistsInArea)
                {
                    return Json(new { success = false, message = "Tên bàn đã tồn tại trong khu vực này." });
                }
                var maxOrder = await _context.Tables.Where(t => t.AreaCode == table.AreaCode).MaxAsync(t => (int?)t.DisplayOrder) ?? 0;
                table.DisplayOrder = maxOrder + 1;
                table.Request = 0;
                table.UpdatedAt = DateTime.Now;
                _context.Add(table);
                await _context.SaveChangesAsync();

                var (updaterId, updaterName) = GetCurrentUserInfo();
                var excludedConnection = GetExcludedConnectionList(__ConnectionId);

                await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveTableUpdate", new
                {
                    action = "create",
                    table = new { tableCode = table.TableCode, nameTable = table.NameTable, isAvailable = table.IsAvailable, isActive = table.IsActive, areaCode = table.AreaCode },
                    areaCode = table.AreaCode,
                    updaterId,
                    updaterName
                });

                // === FIX START: Đã xóa dòng BroadcastAreaUpdate ở đây để tránh 2 thông báo ===
                // await BroadcastAreaUpdate(table.AreaCode, updaterId, updaterName, excludedConnection);
                // === FIX END ===

                return Json(new { success = true, message = "Thêm bàn thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Creating Table: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu dữ liệu bàn." });
            }
        }

        [HttpPost("EditTable/{tableCode}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTable(string tableCode, [FromForm] Table table, [FromForm] string __ConnectionId)
        {
            if (tableCode != table.TableCode)
            {
                return BadRequest(new { message = "Dữ liệu không khớp." });
            }
            var existingTable = await _context.Tables.FindAsync(tableCode);
            if (existingTable == null)
            {
                return NotFound(new { message = "Không tìm thấy bàn để cập nhật." });
            }
            string oldAreaCode = existingTable.AreaCode;
            bool areaChanged = oldAreaCode != table.AreaCode;
            bool nameExistsInSameArea = await _context.Tables.AnyAsync(t =>
                t.TableCode != tableCode &&
                t.AreaCode == table.AreaCode &&
                t.NameTable.ToLower() == table.NameTable.ToLower());

            if (nameExistsInSameArea)
            {
                return Json(new { success = false, message = "Tên bàn đã tồn tại trong khu vực này." });
            }
            try
            {
                existingTable.NameTable = table.NameTable;
                existingTable.AreaCode = table.AreaCode;
                existingTable.IsAvailable = table.IsAvailable;
                existingTable.IsActive = table.IsActive;
                existingTable.UpdatedAt = DateTime.Now;

                _context.Update(existingTable);
                await _context.SaveChangesAsync();

                var (updaterId, updaterName) = GetCurrentUserInfo();
                var excludedConnection = GetExcludedConnectionList(__ConnectionId);

                if (areaChanged)
                {
                    // This scenario is now handled by the MoveTableToArea logic on the client.
                    // However, if direct editing can change the area, this logic is a fallback.
                    // For a cleaner UI, it's better to guide users to use the "Move" feature.
                    // For now, we keep this logic, but the primary path is drag-and-drop.
                    await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveTableUpdate", new { action = "delete", tableCode, areaCode = oldAreaCode, updaterId, updaterName });
                    await BroadcastAreaUpdate(oldAreaCode, updaterId, updaterName, excludedConnection);
                    await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveTableUpdate", new
                    {
                        action = "create",
                        table = new { tableCode = existingTable.TableCode, nameTable = existingTable.NameTable, isAvailable = existingTable.IsAvailable, isActive = existingTable.IsActive, areaCode = existingTable.AreaCode },
                        areaCode = existingTable.AreaCode,
                        updaterId,
                        updaterName
                    });
                    await BroadcastAreaUpdate(existingTable.AreaCode, updaterId, updaterName, excludedConnection);
                }
                else
                {
                    await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveTableUpdate", new
                    {
                        action = "update",
                        table = new { tableCode = existingTable.TableCode, nameTable = existingTable.NameTable, isAvailable = existingTable.IsAvailable, isActive = existingTable.IsActive, areaCode = existingTable.AreaCode },
                        areaCode = existingTable.AreaCode,
                        updaterId,
                        updaterName
                    });
                }
                return Json(new { success = true, message = "Cập nhật bàn thành công!" });
            }
            catch (DbUpdateConcurrencyException)
            {
                return Json(new { success = false, message = "Dữ liệu đã được sửa đổi bởi người khác. Vui lòng tải lại trang." });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Editing Table: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi cập nhật dữ liệu bàn." });
            }
        }

        [HttpPost("DeleteTable")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTable([FromForm] string tableCode, [FromForm] string __ConnectionId)
        {
            if (string.IsNullOrEmpty(tableCode))
            {
                return BadRequest(new { message = "Mã bàn không hợp lệ." });
            }
            var table = await _context.Tables.FindAsync(tableCode);
            if (table == null)
            {
                return Json(new { success = true, message = "Bàn không tồn tại hoặc đã được xóa." });
            }
            bool hasOrders = await _context.Orders.AnyAsync(o => o.TableCode == tableCode);
            if (hasOrders)
            {
                return Json(new { success = false, message = "Không thể xóa bàn này vì đã có hóa đơn liên kết. Vui lòng xử lý các hóa đơn trước." });
            }
            string originalAreaCode = table.AreaCode;
            try
            {
                _context.Tables.Remove(table);
                await _context.SaveChangesAsync();

                var (updaterId, updaterName) = GetCurrentUserInfo();
                var excludedConnection = GetExcludedConnectionList(__ConnectionId);
                await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveTableUpdate", new { action = "delete", tableCode, areaCode = originalAreaCode, updaterId, updaterName });

                // === FIX START: Đã xóa dòng BroadcastAreaUpdate ở đây để tránh 2 thông báo ===
                // await BroadcastAreaUpdate(originalAreaCode, updaterId, updaterName, excludedConnection);
                // === FIX END ===

                return Json(new { success = true, message = "Xóa bàn thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Deleting Table: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi xóa bàn do ràng buộc dữ liệu." });
            }
        }

        [HttpPost("MoveTableToArea")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveTableToArea([FromForm] string tableCode, [FromForm] string newAreaCode, [FromForm] string __ConnectionId)
        {
            if (string.IsNullOrEmpty(tableCode) || string.IsNullOrEmpty(newAreaCode))
            {
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });
            }
            var tableToMove = await _context.Tables.FindAsync(tableCode);
            if (tableToMove == null)
            {
                return NotFound(new { message = "Không tìm thấy bàn để di chuyển." });
            }
            var newArea = await _context.Areas.FindAsync(newAreaCode);
            if (newArea == null)
            {
                return NotFound(new { message = "Không tìm thấy khu vực đích." });
            }
            if (tableToMove.AreaCode == newAreaCode)
            {
                return Json(new { success = true, message = "Bàn đã ở trong khu vực này." });
            }
            bool nameExistsInNewArea = await _context.Tables.AnyAsync(t => t.AreaCode == newAreaCode && t.NameTable.ToLower() == tableToMove.NameTable.ToLower());
            if (nameExistsInNewArea)
            {
                return Json(new { success = false, message = $"Tên bàn '{tableToMove.NameTable}' đã tồn tại trong khu vực '{newArea.Name}'. Vui lòng đổi tên bàn trước khi di chuyển." });
            }

            string oldAreaCode = tableToMove.AreaCode;
            var oldArea = await _context.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.AreaCode == oldAreaCode);

            try
            {
                tableToMove.AreaCode = newAreaCode;
                tableToMove.UpdatedAt = DateTime.Now;
                _context.Update(tableToMove);
                await _context.SaveChangesAsync();

                var (updaterId, updaterName) = GetCurrentUserInfo();
                var excludedConnection = GetExcludedConnectionList(__ConnectionId);

                // *** CHỈ GỬI 1 THÔNG BÁO DUY NHẤT ***
                await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveTableMove", new
                {
                    updaterName,
                    tableCode = tableToMove.TableCode,
                    tableName = tableToMove.NameTable,
                    oldAreaCode = oldAreaCode,
                    oldAreaName = oldArea?.Name ?? "khu vực cũ",
                    newAreaCode = newArea.AreaCode,
                    newAreaName = newArea.Name
                });

                return Json(new { success = true, message = $"Đã di chuyển bàn '{tableToMove.NameTable}' đến khu vực '{newArea.Name}' thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Moving Table: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi cập nhật dữ liệu." });
            }
        }

        [HttpPost("UpdateAreaOrder")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateAreaOrder([FromBody] UpdateAreaOrderRequest request)
        {
            if (request == null || request.OrderedAreaCodes == null || !request.OrderedAreaCodes.Any())
            {
                return BadRequest("Dữ liệu thứ tự không hợp lệ.");
            }
            try
            {
                var areasToUpdate = await _context.Areas.Where(a => request.OrderedAreaCodes.Contains(a.AreaCode)).ToListAsync();
                DateTime now = DateTime.Now;
                for (int i = 0; i < request.OrderedAreaCodes.Count; i++)
                {
                    var area = areasToUpdate.FirstOrDefault(a => a.AreaCode == request.OrderedAreaCodes[i]);
                    if (area != null)
                    {
                        area.DisplayOrder = i + 1;
                        area.UpdatedAt = now;
                    }
                }
                await _context.SaveChangesAsync();

                var (updaterId, updaterName) = GetCurrentUserInfo();
                var excludedConnection = GetExcludedConnectionList(request.__ConnectionId);
                await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveAreaUpdate", new { message = "Area order has been changed.", updaterId, updaterName });

                return Ok(new { success = true, message = "Cập nhật thứ tự khu vực thành công." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating area order: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi cập nhật thứ tự khu vực." });
            }
        }

        [HttpPost("UpdateTableOrder")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTableOrder([FromBody] UpdateTableOrderRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.AreaCode) || request.OrderedTableCodes == null)
            {
                return BadRequest("Dữ liệu yêu cầu không hợp lệ.");
            }
            try
            {
                var tablesToUpdate = await _context.Tables
                    .Where(t => t.AreaCode == request.AreaCode && request.OrderedTableCodes.Contains(t.TableCode))
                    .ToListAsync();

                var orderMap = request.OrderedTableCodes
                    .Select((code, index) => new { Code = code, Index = index })
                    .ToDictionary(item => item.Code, item => item.Index);

                DateTime now = DateTime.Now;
                foreach (var table in tablesToUpdate)
                {
                    if (orderMap.TryGetValue(table.TableCode, out int newIndex))
                    {
                        table.DisplayOrder = newIndex + 1;
                        table.UpdatedAt = now;
                    }
                }

                await _context.SaveChangesAsync();

                var (updaterId, updaterName) = GetCurrentUserInfo();
                var excludedConnection = GetExcludedConnectionList(request.__ConnectionId);
                await _hubContext.Clients.AllExcept(excludedConnection).SendAsync("ReceiveTableUpdate", new
                {
                    action = "reorder",
                    areaCode = request.AreaCode,
                    updaterId,
                    updaterName
                });

                return Ok(new { success = true, message = "Cập nhật thứ tự bàn thành công." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating table order: {ex.Message}");
                return StatusCode(500, new { success = false, message = "Lỗi máy chủ khi cập nhật thứ tự bàn." });
            }
        }

        private async Task BroadcastAreaUpdate(string areaCode, string updaterId, string updaterName, List<string> excludedConnectionIds = null)
        {
            var area = await _context.Areas.Include(a => a.Tables).FirstOrDefaultAsync(a => a.AreaCode == areaCode);
            if (area != null)
            {
                var clients = (excludedConnectionIds != null && excludedConnectionIds.Any())
                                ? _hubContext.Clients.AllExcept(excludedConnectionIds)
                                : _hubContext.Clients.All;

                await clients.SendAsync("ReceiveAreaUpdate", new
                {
                    areaCode = area.AreaCode,
                    tableCount = area.Tables.Count,
                    updaterId,
                    updaterName
                });
            }
        }

        private List<string> GetExcludedConnectionList(string connectionId)
        {
            var excludedList = new List<string>();
            if (!string.IsNullOrEmpty(connectionId))
            {
                excludedList.Add(connectionId);
            }
            return excludedList;
        }
    }

    // Các lớp Request Model để nhận dữ liệu từ Client
    public class UpdateAreaOrderRequest
    {
        public List<string> OrderedAreaCodes { get; set; }
        public string __ConnectionId { get; set; }
    }
    public class UpdateTableOrderRequest
    {
        public string AreaCode { get; set; }
        public List<string> OrderedTableCodes { get; set; }
        public string __ConnectionId { get; set; }
    }
}