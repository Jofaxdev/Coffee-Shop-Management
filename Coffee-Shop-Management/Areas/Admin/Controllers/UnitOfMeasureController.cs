using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Dynamic.Core; // Đảm bảo đã cài đặt package và thêm using
using static Coffee_Shop_Management.Models.AppDbContext; // Giữ lại nếu các class model nằm trong AppDbContext

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class UnitOfMeasureController : Controller
    {
        private readonly AppDbContext _context;
        // Nên sử dụng ILogger trong ứng dụng thực tế thay vì Console.WriteLine
        // private readonly ILogger<UnitOfMeasureController> _logger;

        public UnitOfMeasureController(AppDbContext context /*, ILogger<UnitOfMeasureController> logger */)
        {
            _context = context;
            // _logger = logger;
        }

        // GET: Admin/UnitOfMeasure
        // Action này trả về View chính chứa cả 2 tab
        public IActionResult Index()
        {
            // Đổi tên View thành "UnitOfMeasure/Index.cshtml" khi tạo View
            return View();
        }

        // =========================================================================
        // ACTION LẤY DỮ LIỆU CHO TAB DANH SÁCH CHÍNH (Active/Inactive)
        // =========================================================================
        [HttpPost]
        public async Task<IActionResult> GetDataActive()
        {
            try
            {
                var draw = Request.Form["draw"].FirstOrDefault();
                var start = Request.Form["start"].FirstOrDefault();
                var length = Request.Form["length"].FirstOrDefault();
                var orderColumnIndex = Request.Form["order[0][column]"].FirstOrDefault();
                // Quan trọng: Đảm bảo tên cột trong data-name của <th> khớp với tên thuộc tính Model
                var sortColumn = Request.Form[$"columns[{orderColumnIndex}][name]"].FirstOrDefault();
                var sortColumnDirection = Request.Form["order[0][dir]"].FirstOrDefault();
                var searchValue = Request.Form["search[value]"].FirstOrDefault();
                var statusFilter = Request.Form["statusFilter"].FirstOrDefault();

                int pageSize = length != null ? Convert.ToInt32(length) : 10;
                int skip = start != null ? Convert.ToInt32(start) : 0;
                int recordsFiltered = 0;

                // 1. Query cơ bản: Chỉ lấy các UnitOfMeasure chưa bị xóa mềm
                var unitOfMeasureQuery = _context.UnitOfMeasures
                                                .Where(uom => !uom.DeleteTemp)
                                                .AsQueryable();

                // 2. Tổng số bản ghi TRƯỚC KHI lọc
                int recordsTotal = await unitOfMeasureQuery.CountAsync();

                // 3. Lọc tìm kiếm (theo NameUnitOfMeasure)
                if (!string.IsNullOrEmpty(searchValue))
                {
                    unitOfMeasureQuery = unitOfMeasureQuery.Where(uom => uom.NameUnitOfMeasure.ToLower().Contains(searchValue.ToLower()));
                }

                // 4. Lọc theo trạng thái (IsActive)
                if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "all")
                {
                    if (bool.TryParse(statusFilter, out bool isActive))
                    {
                        unitOfMeasureQuery = unitOfMeasureQuery.Where(uom => uom.IsActive == isActive);
                    }
                }

                // 5. Tổng số bản ghi SAU KHI lọc
                recordsFiltered = await unitOfMeasureQuery.CountAsync();

                // 6. Sắp xếp
                string defaultSortColumn = "CreatedAt"; // Tên thuộc tính trong Model UnitOfMeasure
                string defaultSortDirection = "desc";
                string finalSortColumn = string.IsNullOrEmpty(sortColumn) ? defaultSortColumn : sortColumn;
                string finalSortDirection = string.IsNullOrEmpty(sortColumnDirection) ? defaultSortDirection : sortColumnDirection;
                // Các cột hợp lệ cho phép sắp xếp (khớp với tên thuộc tính Model)
                var validColumns = new[] { "NameUnitOfMeasure", "IsActive", "CreatedAt", "UpdatedAt" };

                if (!validColumns.Contains(finalSortColumn))
                {
                    finalSortColumn = defaultSortColumn;
                    finalSortDirection = defaultSortDirection;
                }

                try
                {
                    string ordering = $"{finalSortColumn} {finalSortDirection}";
                    unitOfMeasureQuery = unitOfMeasureQuery.OrderBy(ordering);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error applying dynamic sort '{finalSortColumn} {finalSortDirection}' for Active UnitOfMeasure data: {ex.Message}");
                    unitOfMeasureQuery = unitOfMeasureQuery.OrderByDescending(uom => uom.CreatedAt); // Fallback
                }

                // 7. Phân trang
                var data = await unitOfMeasureQuery.Skip(skip).Take(pageSize).ToListAsync();

                // 8. Tạo response data (ánh xạ đúng tên thuộc tính)
                var responseData = data.Select(uom => new
                {
                    uom.Id,
                    uom.NameUnitOfMeasure,
                    uom.IsActive,
                    CreatedAt = uom.CreatedAt,
                    UpdatedAt = uom.UpdatedAt
                }).ToList();

                var jsonData = new
                {
                    draw = draw,
                    recordsFiltered = Math.Max(0, recordsFiltered),
                    recordsTotal = Math.Max(0, recordsTotal),
                    data = responseData
                };

                return Ok(jsonData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UnitOfMeasure GetDataActive: {ex.Message}");
                // _logger?.LogError(ex, "Error getting active UnitOfMeasure data.");
                return StatusCode(500, new { error = "Lỗi máy chủ nội bộ khi tải danh sách đơn vị tính." });
            }
        }

        // =========================================================================
        // ACTION LẤY DỮ LIỆU CHO TAB ĐÃ XÓA (Deleted)
        // =========================================================================
        [HttpPost]
        public async Task<IActionResult> GetDataDeleted()
        {
            try
            {
                var draw = Request.Form["draw"].FirstOrDefault();
                var start = Request.Form["start"].FirstOrDefault();
                var length = Request.Form["length"].FirstOrDefault();
                var orderColumnIndex = Request.Form["order[0][column]"].FirstOrDefault();
                var sortColumn = Request.Form[$"columns[{orderColumnIndex}][name]"].FirstOrDefault();
                var sortColumnDirection = Request.Form["order[0][dir]"].FirstOrDefault();
                var searchValue = Request.Form["search[value]"].FirstOrDefault();

                int pageSize = length != null ? Convert.ToInt32(length) : 10;
                int skip = start != null ? Convert.ToInt32(start) : 0;
                int recordsFiltered = 0;

                // 1. Query cơ bản: Chỉ lấy các UnitOfMeasure ĐÃ bị xóa mềm
                var unitOfMeasureQuery = _context.UnitOfMeasures
                                                .Where(uom => uom.DeleteTemp) // Chỉ lấy đã xóa
                                                .AsQueryable();

                // 2. Tổng số bản ghi TRƯỚC KHI lọc
                int recordsTotal = await unitOfMeasureQuery.CountAsync();

                // 3. Lọc tìm kiếm (theo NameUnitOfMeasure)
                if (!string.IsNullOrEmpty(searchValue))
                {
                    unitOfMeasureQuery = unitOfMeasureQuery.Where(uom => uom.NameUnitOfMeasure.ToLower().Contains(searchValue.ToLower()));
                }

                // 4. Tổng số bản ghi SAU KHI lọc
                recordsFiltered = await unitOfMeasureQuery.CountAsync();

                // 5. Sắp xếp
                string defaultSortColumn = "UpdatedAt"; // Ngày xóa (UpdatedAt)
                string defaultSortDirection = "desc";
                string finalSortColumn = string.IsNullOrEmpty(sortColumn) ? defaultSortColumn : sortColumn;
                string finalSortDirection = string.IsNullOrEmpty(sortColumnDirection) ? defaultSortDirection : sortColumnDirection;
                // Các cột hợp lệ (Bỏ IsActive)
                var validColumns = new[] { "NameUnitOfMeasure", "CreatedAt", "UpdatedAt" };

                if (!validColumns.Contains(finalSortColumn))
                {
                    finalSortColumn = defaultSortColumn;
                    finalSortDirection = defaultSortDirection;
                }

                try
                {
                    string ordering = $"{finalSortColumn} {finalSortDirection}";
                    unitOfMeasureQuery = unitOfMeasureQuery.OrderBy(ordering);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error applying dynamic sort '{finalSortColumn} {finalSortDirection}' for Deleted UnitOfMeasure data: {ex.Message}");
                    unitOfMeasureQuery = unitOfMeasureQuery.OrderByDescending(uom => uom.UpdatedAt); // Fallback
                }

                // 6. Phân trang
                var data = await unitOfMeasureQuery.Skip(skip).Take(pageSize).ToListAsync();

                // 7. Tạo response data
                var responseData = data.Select(uom => new
                {
                    uom.Id,
                    uom.NameUnitOfMeasure,
                    CreatedAt = uom.CreatedAt, // Ngày tạo gốc
                    UpdatedAt = uom.UpdatedAt  // Ngày xóa
                }).ToList();

                var jsonData = new
                {
                    draw = draw,
                    recordsFiltered = Math.Max(0, recordsFiltered),
                    recordsTotal = Math.Max(0, recordsTotal),
                    data = responseData
                };

                return Ok(jsonData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UnitOfMeasure GetDataDeleted: {ex.Message}");
                // _logger?.LogError(ex, "Error getting deleted UnitOfMeasure data.");
                return StatusCode(500, new { error = "Lỗi máy chủ nội bộ khi tải danh sách đơn vị tính đã xóa." });
            }
        }

        // =========================================================================
        // CÁC ACTION THÊM / SỬA / XÓA / KHÔI PHỤC
        // =========================================================================

        // POST: Admin/UnitOfMeasure/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("NameUnitOfMeasure,IsActive")] UnitOfMeasure unitOfMeasure)
        {
            if (ModelState.IsValid)
            {
                // Kiểm tra tên trùng lặp trong số những UnitOfMeasure *chưa bị xóa*
                bool nameExists = await _context.UnitOfMeasures
                    .Where(uom => !uom.DeleteTemp && uom.NameUnitOfMeasure.ToLower() == unitOfMeasure.NameUnitOfMeasure.ToLower())
                    .AnyAsync();
                if (nameExists)
                {
                    return Json(new { success = false, message = "Tên đơn vị tính đã tồn tại trong danh sách chính." });
                }
                try
                {
                    unitOfMeasure.CreatedAt = DateTime.Now;
                    unitOfMeasure.UpdatedAt = DateTime.Now;
                    unitOfMeasure.DeleteTemp = false; // Mặc định khi tạo là chưa xóa
                    _context.Add(unitOfMeasure);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Thêm đơn vị tính thành công!" });
                }
                catch (DbUpdateException ex)
                {
                    Console.WriteLine($"Error Creating UnitOfMeasure: {ex.InnerException?.Message ?? ex.Message}");
                    // _logger?.LogError(ex, "Error Creating UnitOfMeasure.");
                    return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu dữ liệu đơn vị tính." });
                }
            }
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            return Json(new { success = false, message = "Dữ liệu không hợp lệ.", errors = errors });
        }

        // GET: Admin/UnitOfMeasure/GetUnitOfMeasureDetails/5
        [HttpGet]
        public async Task<IActionResult> GetUnitOfMeasureDetails(int id)
        {
            var unitOfMeasure = await _context.UnitOfMeasures
                                        .Where(uom => !uom.DeleteTemp && uom.Id == id) // Chỉ lấy cái chưa xóa
                                        .Select(uom => new // Chỉ lấy các trường cần thiết
                                        {
                                            uom.Id,
                                            uom.NameUnitOfMeasure,
                                            uom.IsActive,
                                            uom.CreatedAt
                                        })
                                        .FirstOrDefaultAsync();
            if (unitOfMeasure == null)
            {
                return NotFound(new { message = "Không tìm thấy đơn vị tính hoặc đơn vị này đã bị xóa." });
            }
            return Json(unitOfMeasure); // Trả về JSON trực tiếp
        }

        // POST: Admin/UnitOfMeasure/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,NameUnitOfMeasure,IsActive")] UnitOfMeasure unitOfMeasure)
        {
            if (id != unitOfMeasure.Id) return BadRequest(new { message = "ID không khớp." });

            // Lấy UnitOfMeasure gốc từ DB (chỉ lấy cái chưa xóa)
            var existingUnitOfMeasure = await _context.UnitOfMeasures
                                                    .Where(uom => !uom.DeleteTemp && uom.Id == id)
                                                    .FirstOrDefaultAsync();
            if (existingUnitOfMeasure == null) return NotFound(new { message = "Không tìm thấy đơn vị tính để cập nhật hoặc đơn vị này đã bị xóa." });

            if (ModelState.IsValid)
            {
                // Kiểm tra tên trùng lặp với các UnitOfMeasure *chưa xóa khác*
                bool nameExists = await _context.UnitOfMeasures
                    .Where(uom => !uom.DeleteTemp && uom.Id != id && uom.NameUnitOfMeasure.ToLower() == unitOfMeasure.NameUnitOfMeasure.ToLower())
                    .AnyAsync();
                if (nameExists) return Json(new { success = false, message = "Tên đơn vị tính đã tồn tại trong danh sách chính." });

                try
                {
                    existingUnitOfMeasure.NameUnitOfMeasure = unitOfMeasure.NameUnitOfMeasure;
                    existingUnitOfMeasure.IsActive = unitOfMeasure.IsActive;
                    existingUnitOfMeasure.UpdatedAt = DateTime.Now; // Cập nhật ngày sửa
                    _context.Update(existingUnitOfMeasure);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Cập nhật đơn vị tính thành công!" });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await UnitOfMeasureExistsNotDeleted(unitOfMeasure.Id))
                        return NotFound(new { message = "Đơn vị tính không còn tồn tại." });
                    else
                    {
                        Console.WriteLine($"Concurrency Error Editing UnitOfMeasure ID: {id}");
                        // _logger?.LogWarning("Concurrency Error Editing UnitOfMeasure ID: {Id}", id);
                        return Json(new { success = false, message = "Lỗi trùng lặp dữ liệu khi cập nhật, vui lòng thử lại." });
                    }
                }
                catch (DbUpdateException ex)
                {
                    Console.WriteLine($"Error Editing UnitOfMeasure: {ex.InnerException?.Message ?? ex.Message}");
                    // _logger?.LogError(ex, "Error Editing UnitOfMeasure ID: {Id}", id);
                    return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu dữ liệu đơn vị tính." });
                }
            }
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            return Json(new { success = false, message = "Dữ liệu không hợp lệ.", errors = errors });
        }

        // POST: Admin/UnitOfMeasure/Delete/5 (Xóa mềm)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var unitOfMeasure = await _context.UnitOfMeasures
                                            .Where(uom => !uom.DeleteTemp && uom.Id == id) // Chỉ tìm cái chưa xóa để xóa
                                            .FirstOrDefaultAsync();

            // Nếu không tìm thấy cái chưa xóa -> trả về thành công vì mục tiêu là nó không còn trong DS chính
            if (unitOfMeasure == null) return Json(new { success = true, message = "Đơn vị tính không tồn tại hoặc đã bị xóa." });

            // Kiểm tra xem có sản phẩm nào (chưa bị xóa) đang dùng tên đơn vị tính này không
            bool isInUse = await _context.Products
                                         .Where(p => !p.DeleteTemp && p.UnitOfMeasure.ToLower() == unitOfMeasure.NameUnitOfMeasure.ToLower())
                                         .AnyAsync();
            if (isInUse) return Json(new { success = false, message = "Không thể xóa đơn vị tính này vì đang có sản phẩm (chưa bị xóa) sử dụng nó." });

            try
            {
                unitOfMeasure.DeleteTemp = true; // Đánh dấu xóa mềm
                unitOfMeasure.UpdatedAt = DateTime.Now; // Ghi nhận thời điểm xóa
                _context.Update(unitOfMeasure);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Xóa đơn vị tính thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Soft Deleting UnitOfMeasure: {ex.InnerException?.Message ?? ex.Message}");
                // _logger?.LogError(ex, "Error Soft Deleting UnitOfMeasure ID: {Id}", id);
                return Json(new { success = false, message = "Đã xảy ra lỗi khi xóa dữ liệu đơn vị tính." });
            }
        }

        // POST: Admin/UnitOfMeasure/Restore/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var unitOfMeasure = await _context.UnitOfMeasures
                                            .Where(uom => uom.DeleteTemp && uom.Id == id) // Chỉ tìm cái đã xóa để khôi phục
                                            .FirstOrDefaultAsync();
            if (unitOfMeasure == null) return Json(new { success = false, message = "Không tìm thấy đơn vị tính đã xóa." });

            // Kiểm tra xem có UnitOfMeasure nào *chưa xóa* trùng tên không
            bool nameExists = await _context.UnitOfMeasures
                .Where(uom => !uom.DeleteTemp && uom.NameUnitOfMeasure.ToLower() == unitOfMeasure.NameUnitOfMeasure.ToLower())
                .AnyAsync();
            if (nameExists) return Json(new { success = false, message = $"Không thể khôi phục. Tên đơn vị tính '{unitOfMeasure.NameUnitOfMeasure}' đã tồn tại trong danh sách chính." });

            try
            {
                unitOfMeasure.DeleteTemp = false; // Bỏ đánh dấu xóa mềm
                unitOfMeasure.UpdatedAt = DateTime.Now; // Ghi nhận thời điểm khôi phục
                // Cân nhắc đặt IsActive = true khi khôi phục hoặc giữ nguyên tùy yêu cầu
                // unitOfMeasure.IsActive = true;
                _context.Update(unitOfMeasure);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Khôi phục đơn vị tính thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Restoring UnitOfMeasure: {ex.InnerException?.Message ?? ex.Message}");
                // _logger?.LogError(ex, "Error Restoring UnitOfMeasure ID: {Id}", id);
                return Json(new { success = false, message = "Đã xảy ra lỗi khi khôi phục dữ liệu đơn vị tính." });
            }
        }

        // Helper kiểm tra sự tồn tại của UnitOfMeasure chưa bị xóa
        private async Task<bool> UnitOfMeasureExistsNotDeleted(int id)
        {
            return await _context.UnitOfMeasures.AnyAsync(e => e.Id == id && !e.DeleteTemp);
        }
    }
}