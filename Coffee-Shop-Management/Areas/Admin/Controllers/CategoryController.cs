using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Dynamic.Core; // Đảm bảo đã cài đặt package và thêm using
using static Coffee_Shop_Management.Models.AppDbContext; // <-- Quan trọng: Giữ lại using này nếu các class model nằm trong AppDbContext

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CategoryController : Controller
    {
        private readonly AppDbContext _context;
        // Nên sử dụng ILogger trong ứng dụng thực tế thay vì Console.WriteLine
        // private readonly ILogger<CategoryController> _logger;

        public CategoryController(AppDbContext context /*, ILogger<CategoryController> logger */)
        {
            _context = context;
            // _logger = logger;
        }

        // GET: Admin/Category
        // Action này vẫn trả về View chính chứa cả 2 tab
        public IActionResult Index()
        {
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
                // Xác định cột sắp xếp dựa trên index cột gửi lên và tên cột trong `data-name` ở a `<th>`
                var orderColumnIndex = Request.Form["order[0][column]"].FirstOrDefault();
                var sortColumn = Request.Form[$"columns[{orderColumnIndex}][name]"].FirstOrDefault();
                var sortColumnDirection = Request.Form["order[0][dir]"].FirstOrDefault();
                var searchValue = Request.Form["search[value]"].FirstOrDefault();
                var statusFilter = Request.Form["statusFilter"].FirstOrDefault(); // Bộ lọc trạng thái chỉ dùng cho tab này

                int pageSize = length != null ? Convert.ToInt32(length) : 10;
                int skip = start != null ? Convert.ToInt32(start) : 0;
                int recordsFiltered = 0;

                // 1. Query cơ bản: Chỉ lấy các category chưa bị xóa mềm (DeleteTemp == false)
                var categoryQuery = _context.Categories
                                            .Where(c => !c.DeleteTemp) // Luôn luôn chỉ lấy chưa xóa
                                            .AsQueryable();

                // 2. Tổng số bản ghi TRƯỚC KHI lọc (chỉ tính những cái chưa xóa)
                int recordsTotal = await categoryQuery.CountAsync();

                // 3. Lọc tìm kiếm (theo NameCategory)
                if (!string.IsNullOrEmpty(searchValue))
                {
                    categoryQuery = categoryQuery.Where(c => c.NameCategory.ToLower().Contains(searchValue.ToLower()));
                }

                // 4. Lọc theo trạng thái (IsActive)
                if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "all")
                {
                    if (bool.TryParse(statusFilter, out bool isActive))
                    {
                        categoryQuery = categoryQuery.Where(c => c.IsActive == isActive);
                    }
                }

                // 5. Tổng số bản ghi SAU KHI lọc
                recordsFiltered = await categoryQuery.CountAsync();

                // 6. Sắp xếp
                // Cột mặc định cho danh sách chính là CreatedAt giảm dần
                string defaultSortColumn = "CreatedAt";
                string defaultSortDirection = "desc";
                string finalSortColumn = string.IsNullOrEmpty(sortColumn) ? defaultSortColumn : sortColumn;
                string finalSortDirection = string.IsNullOrEmpty(sortColumnDirection) ? defaultSortDirection : sortColumnDirection;
                // Các cột hợp lệ cho phép sắp xếp ở tab này
                var validColumns = new[] { "NameCategory", "IsActive", "CreatedAt", "UpdatedAt" };

                if (!validColumns.Contains(finalSortColumn))
                {
                    finalSortColumn = defaultSortColumn;
                    finalSortDirection = defaultSortDirection;
                }

                try
                {
                    string ordering = $"{finalSortColumn} {finalSortDirection}";
                    categoryQuery = categoryQuery.OrderBy(ordering);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error applying dynamic sort '{finalSortColumn} {finalSortDirection}' for Active data: {ex.Message}");
                    // Fallback về sắp xếp mặc định nếu có lỗi
                    categoryQuery = categoryQuery.OrderByDescending(c => c.CreatedAt);
                }

                // 7. Phân trang
                var data = await categoryQuery.Skip(skip).Take(pageSize).ToListAsync();

                // 8. Tạo response data
                var responseData = data.Select(c => new
                {
                    c.Id,
                    c.NameCategory,
                    c.IsActive, // Cần trường này để hiển thị badge
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt // Vẫn lấy UpdatedAt để hiển thị ngày sửa cuối
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
                Console.WriteLine($"Error in GetDataActive: {ex.Message}");
                return StatusCode(500, new { error = "Lỗi máy chủ nội bộ khi tải danh sách loại sản phẩm." });
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
                // Xác định cột sắp xếp
                var orderColumnIndex = Request.Form["order[0][column]"].FirstOrDefault();
                var sortColumn = Request.Form[$"columns[{orderColumnIndex}][name]"].FirstOrDefault();
                var sortColumnDirection = Request.Form["order[0][dir]"].FirstOrDefault();
                var searchValue = Request.Form["search[value]"].FirstOrDefault();
                // Không cần statusFilter cho tab đã xóa

                int pageSize = length != null ? Convert.ToInt32(length) : 10;
                int skip = start != null ? Convert.ToInt32(start) : 0;
                int recordsFiltered = 0;

                // 1. Query cơ bản: Chỉ lấy các category ĐÃ bị xóa mềm (DeleteTemp == true)
                var categoryQuery = _context.Categories
                                            .Where(c => c.DeleteTemp) // Luôn luôn chỉ lấy đã xóa
                                            .AsQueryable();

                // 2. Tổng số bản ghi TRƯỚC KHI lọc (chỉ tính những cái đã xóa)
                int recordsTotal = await categoryQuery.CountAsync();

                // 3. Lọc tìm kiếm (theo NameCategory) - Vẫn có thể hữu ích
                if (!string.IsNullOrEmpty(searchValue))
                {
                    categoryQuery = categoryQuery.Where(c => c.NameCategory.ToLower().Contains(searchValue.ToLower()));
                }

                // 4. Không có lọc theo trạng thái (IsActive) ở đây

                // 5. Tổng số bản ghi SAU KHI lọc
                recordsFiltered = await categoryQuery.CountAsync();

                // 6. Sắp xếp
                // Cột mặc định cho danh sách đã xóa là UpdatedAt giảm dần (thường là ngày xóa)
                string defaultSortColumn = "UpdatedAt";
                string defaultSortDirection = "desc";
                string finalSortColumn = string.IsNullOrEmpty(sortColumn) ? defaultSortColumn : sortColumn;
                string finalSortDirection = string.IsNullOrEmpty(sortColumnDirection) ? defaultSortDirection : sortColumnDirection;
                // Các cột hợp lệ cho phép sắp xếp ở tab này (Bỏ IsActive)
                var validColumns = new[] { "NameCategory", "CreatedAt", "UpdatedAt" };

                if (!validColumns.Contains(finalSortColumn))
                {
                    finalSortColumn = defaultSortColumn;
                    finalSortDirection = defaultSortDirection;
                }

                try
                {
                    string ordering = $"{finalSortColumn} {finalSortDirection}";
                    categoryQuery = categoryQuery.OrderBy(ordering);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error applying dynamic sort '{finalSortColumn} {finalSortDirection}' for Deleted data: {ex.Message}");
                    // Fallback về sắp xếp mặc định nếu có lỗi
                    categoryQuery = categoryQuery.OrderByDescending(c => c.UpdatedAt);
                }

                // 7. Phân trang
                var data = await categoryQuery.Skip(skip).Take(pageSize).ToListAsync();

                // 8. Tạo response data
                var responseData = data.Select(c => new
                {
                    c.Id,
                    c.NameCategory,
                    // Không cần trả về IsActive cho bảng đã xóa
                    CreatedAt = c.CreatedAt, // Vẫn có thể hiển thị ngày tạo gốc
                    UpdatedAt = c.UpdatedAt // Ngày xóa/cập nhật cuối
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
                Console.WriteLine($"Error in GetDataDeleted: {ex.Message}");
                return StatusCode(500, new { error = "Lỗi máy chủ nội bộ khi tải danh sách loại sản phẩm đã xóa." });
            }
        }


        // =========================================================================
        // CÁC ACTION THÊM / SỬA / XÓA / KHÔI PHỤC (Giữ nguyên logic phần lớn)
        // =========================================================================

        // POST: Admin/Category/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("NameCategory,IsActive")] Category category)
        {
            if (ModelState.IsValid)
            {
                // Kiểm tra tên trùng lặp trong số những category *chưa bị xóa*
                bool nameExists = await _context.Categories
                                               .Where(c => !c.DeleteTemp && c.NameCategory.ToLower() == category.NameCategory.ToLower())
                                               .AnyAsync();
                if (nameExists)
                {
                    return Json(new { success = false, message = "Tên loại sản phẩm đã tồn tại trong danh sách chính." });
                }
                try
                {
                    category.CreatedAt = DateTime.Now;
                    category.UpdatedAt = DateTime.Now;
                    category.DeleteTemp = false; // Mặc định khi tạo là chưa xóa
                    _context.Add(category);
                    await _context.SaveChangesAsync();
                    // Client sẽ tự reload lại bảng Active sau khi nhận success=true
                    return Json(new { success = true, message = "Thêm loại sản phẩm thành công!" });
                }
                catch (DbUpdateException ex)
                {
                    Console.WriteLine($"Error Creating Category: {ex.InnerException?.Message ?? ex.Message}");
                    return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu dữ liệu." });
                }
            }
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            return Json(new { success = false, message = "Dữ liệu không hợp lệ.", errors = errors });
        }

        // GET: Admin/Category/GetCategoryDetails/5
        // Lấy chi tiết thường dành cho việc Edit, nên chỉ lấy cái chưa xóa
        [HttpGet]
        public async Task<IActionResult> GetCategoryDetails(int id)
        {
            var category = await _context.Categories
                                         .Where(c => !c.DeleteTemp && c.Id == id) // Chỉ lấy cái chưa xóa
                                         .Select(c => new // Chỉ lấy các trường cần thiết
                                         {
                                             c.Id,
                                             c.NameCategory,
                                             c.IsActive,
                                             c.CreatedAt
                                         })
                                         .FirstOrDefaultAsync();
            if (category == null)
            {
                return NotFound(new { message = "Không tìm thấy loại sản phẩm hoặc loại này đã bị xóa." });
            }
            return Json(category); // Trả về JSON trực tiếp
        }

        // POST: Admin/Category/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,NameCategory,IsActive")] Category category)
        {
            if (id != category.Id) return BadRequest(new { message = "ID không khớp." });

            // Lấy category gốc từ DB (chỉ lấy cái chưa xóa)
            var existingCategory = await _context.Categories
                                                .Where(c => !c.DeleteTemp && c.Id == id)
                                                .FirstOrDefaultAsync();
            if (existingCategory == null) return NotFound(new { message = "Không tìm thấy loại sản phẩm để cập nhật hoặc loại này đã bị xóa." });

            if (ModelState.IsValid)
            {
                // Kiểm tra tên trùng lặp với các category *chưa xóa khác*
                bool nameExists = await _context.Categories
                                               .Where(c => !c.DeleteTemp && c.Id != id && c.NameCategory.ToLower() == category.NameCategory.ToLower())
                                               .AnyAsync();
                if (nameExists) return Json(new { success = false, message = "Tên loại sản phẩm đã tồn tại trong danh sách chính." });

                try
                {
                    existingCategory.NameCategory = category.NameCategory;
                    existingCategory.IsActive = category.IsActive;
                    existingCategory.UpdatedAt = DateTime.Now; // Cập nhật ngày sửa
                    // Không thay đổi DeleteTemp ở đây
                    _context.Update(existingCategory);
                    await _context.SaveChangesAsync();
                    // Client sẽ tự reload lại bảng Active sau khi nhận success=true
                    return Json(new { success = true, message = "Cập nhật loại sản phẩm thành công!" });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await CategoryExistsNotDeleted(category.Id))
                        return NotFound(new { message = "Loại sản phẩm không còn tồn tại." });
                    else
                    {
                        Console.WriteLine($"Concurrency Error Editing Category ID: {id}");
                        return Json(new { success = false, message = "Lỗi trùng lặp dữ liệu, vui lòng thử lại." });
                    }
                }
                catch (DbUpdateException ex)
                {
                    Console.WriteLine($"Error Editing Category: {ex.InnerException?.Message ?? ex.Message}");
                    return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu dữ liệu." });
                }
            }
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            return Json(new { success = false, message = "Dữ liệu không hợp lệ.", errors = errors });
        }

        // POST: Admin/Category/Delete/5 (Xóa mềm)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _context.Categories
                                         .Where(c => !c.DeleteTemp && c.Id == id) // Chỉ tìm cái chưa xóa để xóa
                                         .FirstOrDefaultAsync();
            // Nếu không tìm thấy cái chưa xóa -> trả về thành công vì mục tiêu là nó không còn trong DS chính
            if (category == null) return Json(new { success = true, message = "Loại sản phẩm không tồn tại hoặc đã bị xóa." });

            // Kiểm tra xem có sản phẩm nào (chưa bị xóa) đang dùng loại này không
            bool isInUse = await _context.ProductCategorys
                                         .Include(pc => pc.Product) // Nạp thông tin Product liên quan
                                         .Where(pc => pc.CategoryId == id && pc.Product != null && !pc.Product.DeleteTemp)
                                         .AnyAsync();
            if (isInUse) return Json(new { success = false, message = "Không thể xóa loại này vì đang có sản phẩm (chưa bị xóa) thuộc loại này." });

            try
            {
                category.DeleteTemp = true; // Đánh dấu xóa mềm
                category.UpdatedAt = DateTime.Now; // Ghi nhận thời điểm xóa
                _context.Update(category);
                await _context.SaveChangesAsync();
                // Client sẽ tự reload lại bảng Active sau khi nhận success=true
                return Json(new { success = true, message = "Xóa loại sản phẩm thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Soft Deleting Category: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi xóa dữ liệu." });
            }
        }

        // POST: Admin/Category/Restore/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var category = await _context.Categories
                                         .Where(c => c.DeleteTemp && c.Id == id) // Chỉ tìm cái đã xóa để khôi phục
                                         .FirstOrDefaultAsync();
            if (category == null) return Json(new { success = false, message = "Không tìm thấy loại sản phẩm đã xóa." });

            // Kiểm tra xem có category nào *chưa xóa* trùng tên không
            bool nameExists = await _context.Categories
                                           .Where(c => !c.DeleteTemp && c.NameCategory.ToLower() == category.NameCategory.ToLower())
                                           .AnyAsync();
            if (nameExists) return Json(new { success = false, message = $"Không thể khôi phục. Tên loại '{category.NameCategory}' đã tồn tại trong danh sách chính." });

            try
            {
                category.DeleteTemp = false; // Bỏ đánh dấu xóa mềm
                category.UpdatedAt = DateTime.Now; // Ghi nhận thời điểm khôi phục
                // Có thể cân nhắc set IsActive = true khi khôi phục hoặc giữ nguyên trạng thái cũ tùy yêu cầu
                // category.IsActive = true;
                _context.Update(category);
                await _context.SaveChangesAsync();
                // Client sẽ tự reload lại bảng Deleted sau khi nhận success=true
                return Json(new { success = true, message = "Khôi phục loại sản phẩm thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Restoring Category: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi khôi phục dữ liệu." });
            }
        }

        // Helper kiểm tra sự tồn tại của category chưa bị xóa
        private async Task<bool> CategoryExistsNotDeleted(int id)
        {
            return await _context.Categories.AnyAsync(e => e.Id == id && !e.DeleteTemp);
        }
    }
}