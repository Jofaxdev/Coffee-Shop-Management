using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering; // Needed for SelectList
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Linq.Dynamic.Core; // Ensure installed
using System.Threading.Tasks;
using static Coffee_Shop_Management.Models.AppDbContext; // If models are nested

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AreaTableController : Controller
    {
        private readonly AppDbContext _context;
        // private readonly ILogger<AreaTableController> _logger; // Optional

        public AreaTableController(AppDbContext context /*, ILogger<AreaTableController> logger */)
        {
            _context = context;
            // _logger = logger;
        }

        // GET: Admin/AreaTable
        public IActionResult Index()
        {
            // We might pre-load areas for the Table filter dropdown here,
            // but doing it via AJAX on view load is often better for performance.
            return View();
        }

        // =========================================================================
        // AREA MANAGEMENT ACTIONS
        // =========================================================================

        // POST: Admin/AreaTable/GetDataAreas
        [HttpPost]
        public async Task<IActionResult> GetDataAreas()
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

                var areaQuery = _context.Areas.AsQueryable(); // No DeleteTemp for Area

                int recordsTotal = await areaQuery.CountAsync();

                if (!string.IsNullOrEmpty(searchValue))
                {
                    areaQuery = areaQuery.Where(a => a.Name.ToLower().Contains(searchValue.ToLower()) ||
                                                     (a.Description != null && a.Description.ToLower().Contains(searchValue.ToLower())));
                }

                recordsFiltered = await areaQuery.CountAsync();

                // Default sort: CreatedAt descending
                string defaultSortColumn = "CreatedAt";
                string defaultSortDirection = "desc";
                string finalSortColumn = string.IsNullOrEmpty(sortColumn) ? defaultSortColumn : sortColumn;
                string finalSortDirection = string.IsNullOrEmpty(sortColumnDirection) ? defaultSortDirection : sortColumnDirection;
                var validColumns = new[] { "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt", "TableCount" }; // Allow sorting by TableCount

                if (!validColumns.Contains(finalSortColumn))
                {
                    finalSortColumn = defaultSortColumn;
                    finalSortDirection = defaultSortDirection;
                }

                // Handle sorting by TableCount specifically
                if (finalSortColumn == "TableCount")
                {
                    // Use OrderBy directly before applying dynamic sort for other columns
                    if (finalSortDirection.ToLower() == "asc")
                    {
                        areaQuery = areaQuery.OrderBy(a => a.Tables.Count());
                    }
                    else
                    {
                        areaQuery = areaQuery.OrderByDescending(a => a.Tables.Count());
                    }
                    // Apply secondary sorting if needed, or skip dynamic sort if TableCount is primary
                    // For simplicity, we'll let TableCount be the primary sort if chosen.
                }
                else
                {
                    try
                    {
                        string ordering = $"{finalSortColumn} {finalSortDirection}";
                        areaQuery = areaQuery.OrderBy(ordering);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error applying dynamic sort '{finalSortColumn} {finalSortDirection}' for Areas: {ex.Message}");
                        areaQuery = areaQuery.OrderByDescending(a => a.CreatedAt); // Fallback
                    }
                }


                var data = await areaQuery
                                .Skip(skip)
                                .Take(pageSize)
                                .Select(a => new // Select specific fields + Table Count
                                {
                                    a.Id,
                                    a.Name,
                                    a.Description,
                                    a.IsActive,
                                    TableCount = a.Tables.Count(), // Calculate table count
                                    CreatedAt = a.CreatedAt,
                                    UpdatedAt = a.UpdatedAt
                                })
                                .ToListAsync();

                var jsonData = new
                {
                    draw = draw,
                    recordsFiltered = Math.Max(0, recordsFiltered),
                    recordsTotal = Math.Max(0, recordsTotal),
                    data = data
                };
                return Ok(jsonData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDataAreas: {ex.Message}");
                return StatusCode(500, new { error = "Lỗi máy chủ nội bộ khi tải danh sách khu vực." });
            }
        }

        // POST: Admin/AreaTable/CreateArea
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateArea([FromForm][Bind("Name,Description,IsActive")] Area area)
        {
            if (ModelState.IsValid)
            {
                bool nameExists = await _context.Areas.AnyAsync(a => a.Name.ToLower() == area.Name.ToLower());
                if (nameExists)
                {
                    return Json(new { success = false, message = "Tên khu vực đã tồn tại." });
                }

                try
                {
                    area.CreatedAt = DateTime.Now;
                    area.UpdatedAt = DateTime.Now;
                    _context.Add(area);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Thêm khu vực thành công!" });
                }
                catch (DbUpdateException ex)
                {
                    Console.WriteLine($"Error Creating Area: {ex.InnerException?.Message ?? ex.Message}");
                    return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu dữ liệu." });
                }
            }
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            return Json(new { success = false, message = "Dữ liệu không hợp lệ.", errors = errors });
        }

        // GET: Admin/AreaTable/GetAreaDetails/5
        [HttpGet]
        public async Task<IActionResult> GetAreaDetails(int id)
        {
            var area = await _context.Areas
                .Where(a => a.Id == id)
                .Select(a => new { a.Id, a.Name, a.Description, a.IsActive, a.CreatedAt })
                .FirstOrDefaultAsync();

            if (area == null)
            {
                return NotFound(new { message = "Không tìm thấy khu vực." });
            }
            return Json(area);
        }

        // POST: Admin/AreaTable/EditArea/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditArea(int id, [FromForm][Bind("Id,Name,Description,IsActive")] Area area)
        {
            if (id != area.Id) return BadRequest(new { message = "ID không khớp." });

            var existingArea = await _context.Areas.FindAsync(id);
            if (existingArea == null) return NotFound(new { message = "Không tìm thấy khu vực để cập nhật." });

            if (ModelState.IsValid)
            {
                bool nameExists = await _context.Areas.AnyAsync(a => a.Id != id && a.Name.ToLower() == area.Name.ToLower());
                if (nameExists) return Json(new { success = false, message = "Tên khu vực đã tồn tại." });

                try
                {
                    existingArea.Name = area.Name;
                    existingArea.Description = area.Description;
                    existingArea.IsActive = area.IsActive;
                    existingArea.UpdatedAt = DateTime.Now;

                    _context.Update(existingArea);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Cập nhật khu vực thành công!" });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Areas.AnyAsync(e => e.Id == area.Id))
                        return NotFound(new { message = "Khu vực không còn tồn tại." });
                    else
                    {
                        Console.WriteLine($"Concurrency Error Editing Area ID: {id}");
                        return Json(new { success = false, message = "Lỗi trùng lặp dữ liệu, vui lòng thử lại." });
                    }
                }
                catch (DbUpdateException ex)
                {
                    Console.WriteLine($"Error Editing Area: {ex.InnerException?.Message ?? ex.Message}");
                    return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu dữ liệu." });
                }
            }
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            return Json(new { success = false, message = "Dữ liệu không hợp lệ.", errors = errors });
        }

        // POST: Admin/AreaTable/DeleteArea/5 (PHYSICAL DELETE)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteArea(int id)
        {
            var area = await _context.Areas.FindAsync(id);
            if (area == null) return Json(new { success = true, message = "Khu vực không tồn tại hoặc đã được xóa." }); // Considered success if not found

            // Check for related Tables (Enforce Restrict constraint)
            bool hasTables = await _context.Tables.AnyAsync(t => t.AreaId == id);
            if (hasTables)
            {
                return Json(new { success = false, message = "Không thể xóa khu vực này vì vẫn còn bàn thuộc khu vực. Vui lòng xóa hoặc chuyển bàn trước." });
            }

            try
            {
                _context.Areas.Remove(area);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Xóa khu vực thành công!" });
            }
            catch (DbUpdateException ex) // Catch potential FK issues if check failed somehow
            {
                Console.WriteLine($"Error Deleting Area: {ex.InnerException?.Message ?? ex.Message}");
                // More specific error might be needed based on SQL error code if possible
                return Json(new { success = false, message = "Đã xảy ra lỗi khi xóa khu vực. Có thể do ràng buộc dữ liệu." });
            }
        }

        // =========================================================================
        // TABLE MANAGEMENT ACTIONS
        // =========================================================================

        // GET: Helper to get active areas for dropdowns
        [HttpGet]
        public async Task<IActionResult> GetActiveAreasForDropdown()
        {
            var areas = await _context.Areas
                                .Where(a => a.IsActive)
                                .OrderBy(a => a.Name)
                                .Select(a => new { id = a.Id, text = a.Name }) // Format for Select2 or simple dropdown
                                .ToListAsync();
            return Json(areas);
        }


        // POST: Admin/AreaTable/GetDataTables
        [HttpPost]
        public async Task<IActionResult> GetDataTables()
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
                // Custom filters from View
                var areaFilter = Request.Form["areaFilter"].FirstOrDefault();
                var statusFilter = Request.Form["statusFilter"].FirstOrDefault(); // Combined filter for IsAvailable/IsActive

                int pageSize = length != null ? Convert.ToInt32(length) : 10;
                int skip = start != null ? Convert.ToInt32(start) : 0;
                int recordsFiltered = 0;

                // Include Area for displaying name and filtering/sorting
                var tableQuery = _context.Tables.Include(t => t.Area).AsQueryable();

                int recordsTotal = await tableQuery.CountAsync();

                // Apply Area Filter
                if (!string.IsNullOrEmpty(areaFilter) && int.TryParse(areaFilter, out int areaId) && areaId > 0)
                {
                    tableQuery = tableQuery.Where(t => t.AreaId == areaId);
                }

                // Apply Status Filter (IsAvailable / IsActive)
                if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "all")
                {
                    switch (statusFilter)
                    {
                        case "available_true":
                            tableQuery = tableQuery.Where(t => t.IsAvailable == true);
                            break;
                        case "available_false":
                            tableQuery = tableQuery.Where(t => t.IsAvailable == false);
                            break;
                        case "active_true":
                            tableQuery = tableQuery.Where(t => t.IsActive == true);
                            break;
                        case "active_false":
                            tableQuery = tableQuery.Where(t => t.IsActive == false);
                            break;
                    }
                }


                // Apply Search Filter (Table Name or Area Name)
                if (!string.IsNullOrEmpty(searchValue))
                {
                    string lowerSearch = searchValue.ToLower();
                    tableQuery = tableQuery.Where(t => t.NameTable.ToLower().Contains(lowerSearch) ||
                                                     (t.Area != null && t.Area.Name.ToLower().Contains(lowerSearch)));
                }

                recordsFiltered = await tableQuery.CountAsync();

                // Sorting
                string defaultSortColumn = "NameTable"; // Default sort by Table Name
                string defaultSortDirection = "asc";
                string finalSortColumn = string.IsNullOrEmpty(sortColumn) ? defaultSortColumn : sortColumn;
                string finalSortDirection = string.IsNullOrEmpty(sortColumnDirection) ? defaultSortDirection : sortColumnDirection;
                // Valid columns including related data
                var validColumns = new[] { "NameTable", "AreaName", "IsAvailable", "IsActive", "Request" };

                if (!validColumns.Contains(finalSortColumn))
                {
                    finalSortColumn = defaultSortColumn;
                    finalSortDirection = defaultSortDirection;
                }

                try
                {
                    // Adjust for related data sorting
                    string ordering = finalSortColumn switch
                    {
                        "AreaName" => $"Area.Name {finalSortDirection}", // Sort by related property
                        _ => $"{finalSortColumn} {finalSortDirection}" // Default sort
                    };
                    tableQuery = tableQuery.OrderBy(ordering);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error applying dynamic sort '{finalSortColumn} {finalSortDirection}' for Tables: {ex.Message}");
                    tableQuery = tableQuery.OrderBy(t => t.NameTable); // Fallback
                }


                var data = await tableQuery
                                .Skip(skip)
                                .Take(pageSize)
                                .Select(t => new
                                {
                                    t.Id,
                                    t.NameTable,
                                    AreaId = t.AreaId,
                                    AreaName = t.Area != null ? t.Area.Name : "N/A", // Handle null Area if necessary
                                    t.IsAvailable,
                                    t.IsActive,
                                    t.Request
                                })
                                .ToListAsync();

                var jsonData = new
                {
                    draw = draw,
                    recordsFiltered = Math.Max(0, recordsFiltered),
                    recordsTotal = Math.Max(0, recordsTotal),
                    data = data
                };
                return Ok(jsonData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDataTables: {ex.Message}");
                return StatusCode(500, new { error = "Lỗi máy chủ nội bộ khi tải danh sách bàn." });
            }
        }

        // POST: Admin/AreaTable/CreateTable
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTable([FromForm][Bind("NameTable,AreaId,IsAvailable,IsActive")] Table table)
        {
            // Ensure AreaId exists and is active before creating
            var areaExists = await _context.Areas.AnyAsync(a => a.Id == table.AreaId && a.IsActive);
            if (!areaExists)
            {
                ModelState.AddModelError("AreaId", "Khu vực được chọn không hợp lệ hoặc không hoạt động.");
            }

            // Check for duplicate name within the SAME Area
            if (ModelState.IsValid) // Check after potentially adding AreaId error
            {
                bool nameExistsInArea = await _context.Tables.AnyAsync(t => t.AreaId == table.AreaId && t.NameTable.ToLower() == table.NameTable.ToLower());
                if (nameExistsInArea)
                {
                    ModelState.AddModelError("NameTable", "Tên bàn đã tồn tại trong khu vực này.");
                }
            }


            if (ModelState.IsValid)
            {
                try
                {
                    table.Request = 0; // Default value
                    // CreatedAt/UpdatedAt are not in the Table model, add if needed
                    _context.Add(table);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Thêm bàn thành công!" });
                }
                catch (DbUpdateException ex)
                {
                    Console.WriteLine($"Error Creating Table: {ex.InnerException?.Message ?? ex.Message}");
                    return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu dữ liệu." });
                }
            }
            var errors = ModelState.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            return Json(new { success = false, message = "Dữ liệu không hợp lệ.", errors = errors }); // Return structured errors
        }

        // GET: Admin/AreaTable/GetTableDetails/5
        [HttpGet]
        public async Task<IActionResult> GetTableDetails(int id)
        {
            var table = await _context.Tables
                .Where(t => t.Id == id)
                .Select(t => new
                {
                    t.Id,
                    t.NameTable,
                    t.AreaId, // Need AreaId to pre-select dropdown
                    t.IsAvailable,
                    t.IsActive
                    // Include other fields if needed for display in modal
                })
                .FirstOrDefaultAsync();

            if (table == null)
            {
                return NotFound(new { message = "Không tìm thấy bàn." });
            }
            return Json(table);
        }

        // POST: Admin/AreaTable/EditTable/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTable(int id, [FromForm][Bind("Id,NameTable,AreaId,IsAvailable,IsActive")] Table table)
        {
            if (id != table.Id) return BadRequest(new { message = "ID không khớp." });

            var existingTable = await _context.Tables.FindAsync(id);
            if (existingTable == null) return NotFound(new { message = "Không tìm thấy bàn để cập nhật." });

            // Ensure AreaId exists and is active before saving
            var areaExists = await _context.Areas.AnyAsync(a => a.Id == table.AreaId && a.IsActive);
            if (!areaExists)
            {
                ModelState.AddModelError("AreaId", "Khu vực được chọn không hợp lệ hoặc không hoạt động.");
            }

            // Check for duplicate name within the SAME Area, EXCLUDING self
            if (ModelState.IsValid) // Check after potentially adding AreaId error
            {
                bool nameExistsInArea = await _context.Tables
                                            .AnyAsync(t => t.Id != id && // Exclude self
                                                         t.AreaId == table.AreaId &&
                                                         t.NameTable.ToLower() == table.NameTable.ToLower());
                if (nameExistsInArea)
                {
                    ModelState.AddModelError("NameTable", "Tên bàn đã tồn tại trong khu vực này.");
                }
            }


            if (ModelState.IsValid)
            {
                try
                {
                    existingTable.NameTable = table.NameTable;
                    existingTable.AreaId = table.AreaId;
                    existingTable.IsAvailable = table.IsAvailable;
                    existingTable.IsActive = table.IsActive;
                    // Update 'UpdatedAt' if the model has it

                    _context.Update(existingTable);
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Cập nhật bàn thành công!" });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Tables.AnyAsync(e => e.Id == table.Id))
                        return NotFound(new { message = "Bàn không còn tồn tại." });
                    else
                    {
                        Console.WriteLine($"Concurrency Error Editing Table ID: {id}");
                        return Json(new { success = false, message = "Lỗi trùng lặp dữ liệu, vui lòng thử lại." });
                    }
                }
                catch (DbUpdateException ex)
                {
                    Console.WriteLine($"Error Editing Table: {ex.InnerException?.Message ?? ex.Message}");
                    return Json(new { success = false, message = "Đã xảy ra lỗi khi lưu dữ liệu." });
                }
            }
            var errors = ModelState.ToDictionary(
                   kvp => kvp.Key,
                   kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()
               );
            return Json(new { success = false, message = "Dữ liệu không hợp lệ.", errors = errors });
        }

        // POST: Admin/AreaTable/DeleteTable/5 (PHYSICAL DELETE)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTable(int id)
        {
            var table = await _context.Tables.FindAsync(id);
            if (table == null) return Json(new { success = true, message = "Bàn không tồn tại hoặc đã được xóa." });

            // Check for related Orders (Enforce Restrict constraint)
            // Note: Adjust based on your actual Order model and FK relationship
            bool hasOrders = await _context.Orders.AnyAsync(o => o.TableId == id);
            if (hasOrders)
            {
                return Json(new { success = false, message = "Không thể xóa bàn này vì đã có hóa đơn liên kết. Vui lòng xử lý hóa đơn trước." });
            }

            try
            {
                _context.Tables.Remove(table);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Xóa bàn thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Deleting Table: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi xóa bàn. Có thể do ràng buộc dữ liệu." });
            }
        }
    }
}