using Coffee_Shop_Management.Areas.Admin.ViewModels;
using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using static Coffee_Shop_Management.Models.AppDbContext; // Giữ nguyên nếu bạn có class AppDbContext lồng bên trong

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ProductController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ProductController(AppDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        public IActionResult Index()
        {
            ViewData["Title"] = "Quản lý Sản phẩm";
            return View();
        }

        // Action Create (GIỮ NGUYÊN)
        [HttpPost]
        //[ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromForm] ProductVM model)
        {
            // --- GIỮ NGUYÊN CODE CREATE ---
            // Kiểm tra xem CategoryId có hợp lệ không trước khi kiểm tra ModelState
            if (model.IdCategory <= 0)
            {
                ModelState.AddModelError(nameof(model.IdCategory), "Vui lòng chọn loại sản phẩm hợp lệ.");
            }
            // ---- BỎ REQUIRED CHO IMAGE Ở ĐÂY ----
            // if (model.ImageProduct == null || model.ImageProduct.Length == 0)
            // {
            //     ModelState.AddModelError(nameof(model.ImageProduct), "Vui lòng chọn hình ảnh.");
            // }
            // ---- THAY BẰNG KIỂM TRA BÊN DƯỚI ----
            if (model.ImageProduct == null || model.ImageProduct.Length == 0)
            {
                // Ảnh là bắt buộc khi tạo mới
                ModelState.AddModelError(nameof(model.ImageProduct), "Vui lòng chọn hình ảnh.");
            }
            else // Chỉ kiểm tra extension và size nếu có file được upload
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var extension = Path.GetExtension(model.ImageProduct.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                {
                    ModelState.AddModelError(nameof(model.ImageProduct), "Chỉ chấp nhận file ảnh (.jpg, .jpeg, .png, .gif).");
                }
                const long maxFileSize = 5 * 1024 * 1024;
                if (model.ImageProduct.Length > maxFileSize)
                {
                    ModelState.AddModelError(nameof(model.ImageProduct), $"Kích thước file không được vượt quá {maxFileSize / 1024 / 1024}MB.");
                }
            }


            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    string? uniqueFileName = null;
                    // Chỉ xử lý ảnh nếu có upload file mới (bắt buộc khi create)
                    if (model.ImageProduct != null && model.ImageProduct.Length > 0)
                    {
                        string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "product");
                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }
                        uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(model.ImageProduct.FileName);
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await model.ImageProduct.CopyToAsync(fileStream);
                        }
                    }
                    else
                    {
                        // Lỗi này đã được kiểm tra bởi ModelState nên không cần xử lý lại
                        // return Json(new { success = false, message = "Lỗi: Hình ảnh là bắt buộc khi tạo mới." });
                    }


                    var product = new Product
                    {
                        Name = model.NameProduct,
                        CostPrice = model.OriginalPrice,
                        UnitPrice = model.Price,
                        UnitOfMeasure = model.Unit,
                        Description = model.Description ?? string.Empty,
                        // uniqueFileName không thể null ở đây nếu ModelState hợp lệ và code chạy đúng
                        ImageUrl = uniqueFileName!,
                        IsActive = model.InStock == "true",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _context.Products.Add(product);
                    await _context.SaveChangesAsync(); // Lưu SP để lấy ID

                    var productCategory = new ProductCategory
                    {
                        ProductId = product.ProductId,
                        CategoryId = model.IdCategory
                    };
                    _context.ProductCategorys.Add(productCategory);
                    await _context.SaveChangesAsync(); // Lưu quan hệ

                    await transaction.CommitAsync();
                    return Json(new { success = true, message = "Thêm sản phẩm thành công." });
                }
                catch (DbUpdateException dbEx)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine($"Lỗi DB khi tạo sản phẩm: {dbEx.InnerException?.Message ?? dbEx.Message}");
                    return Json(new { success = false, message = "Lỗi cơ sở dữ liệu khi thêm sản phẩm. Dữ liệu có thể không hợp lệ." });
                }
                catch (IOException ioEx)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine($"Lỗi IO khi lưu ảnh: {ioEx.Message}");
                    return Json(new { success = false, message = "Lỗi khi lưu hình ảnh sản phẩm." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine($"Lỗi chung khi tạo sản phẩm: {ex.ToString()}");
                    return Json(new { success = false, message = "Đã xảy ra lỗi không mong muốn khi thêm sản phẩm." });
                }
            }
            else
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors)
                                              .Select(e => e.ErrorMessage)
                                              .ToList();
                return Json(new { success = false, message = "Dữ liệu nhập không hợp lệ.", errors = errors });
            }
        }

        // *** NEW ACTION: GetProductDetails ***
        [HttpGet]
        public async Task<IActionResult> GetProductDetails(int id)
        {
            if (id <= 0)
            {
                return BadRequest(new { message = "ID sản phẩm không hợp lệ." });
            }

            try
            {
                var product = await _context.Products
                    .AsNoTracking()
                    .Include(p => p.ProductCategorys) // Include để lấy CategoryId
                    .Where(p => p.ProductId == id)
                    .Select(p => new // Chọn các trường cần thiết cho form edit
                    {
                        p.ProductId,
                        NameProduct = p.Name,
                        OriginalPrice = p.CostPrice,
                        Price = p.UnitPrice,
                        Unit = p.UnitOfMeasure,
                        p.Description,
                        ImageUrl = p.ImageUrl, // Chỉ trả về tên file
                        InStock = p.IsActive, // Trả về bool
                        // Lấy CategoryId từ bảng ProductCategorys
                        IdCategory = p.ProductCategorys.Select(pc => pc.CategoryId).FirstOrDefault()
                    })
                    .FirstOrDefaultAsync();

                if (product == null)
                {
                    return NotFound(new { message = "Không tìm thấy sản phẩm." });
                }

                return Json(product);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi GetProductDetails: {ex.ToString()}");
                return StatusCode(500, new { message = "Lỗi máy chủ khi lấy chi tiết sản phẩm." });
            }
        }


        // *** NEW ACTION: Edit ***
        [HttpPost]
        //[ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [FromForm] ProductVM model)
        {
            if (id <= 0)
            {
                return BadRequest(new { success = false, message = "ID sản phẩm không hợp lệ." });
            }

            // --- VALIDATION (Tương tự Create nhưng ImageProduct không bắt buộc) ---
            if (model.IdCategory <= 0)
            {
                ModelState.AddModelError(nameof(model.IdCategory), "Vui lòng chọn loại sản phẩm hợp lệ.");
            }

            // Kiểm tra ImageProduct chỉ khi có file được upload
            if (model.ImageProduct != null && model.ImageProduct.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                var extension = Path.GetExtension(model.ImageProduct.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                {
                    ModelState.AddModelError(nameof(model.ImageProduct), "Chỉ chấp nhận file ảnh (.jpg, .jpeg, .png, .gif).");
                }
                const long maxFileSize = 5 * 1024 * 1024; // 5MB
                if (model.ImageProduct.Length > maxFileSize)
                {
                    ModelState.AddModelError(nameof(model.ImageProduct), $"Kích thước file không được vượt quá {maxFileSize / 1024 / 1024}MB.");
                }
            }
            // Bỏ qua kiểm tra ImageProduct nếu không có file mới


            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Lấy sản phẩm hiện có từ DB, bao gồm cả Category liên quan
                    var product = await _context.Products
                                        .Include(p => p.ProductCategorys)
                                        .FirstOrDefaultAsync(p => p.ProductId == id);

                    if (product == null)
                    {
                        await transaction.RollbackAsync();
                        return NotFound(new { success = false, message = "Không tìm thấy sản phẩm để cập nhật." });
                    }

                    string? oldImageUrl = product.ImageUrl; // Lưu lại ảnh cũ để xóa nếu cần
                    string? newUniqueFileName = null;

                    // Xử lý Upload Ảnh Mới (nếu có)
                    if (model.ImageProduct != null && model.ImageProduct.Length > 0)
                    {
                        string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "product");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                        newUniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(model.ImageProduct.FileName);
                        string filePath = Path.Combine(uploadsFolder, newUniqueFileName);
                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await model.ImageProduct.CopyToAsync(fileStream);
                        }
                        product.ImageUrl = newUniqueFileName; // Cập nhật ảnh mới cho sản phẩm
                    }
                    // Nếu không có ảnh mới, giữ nguyên ảnh cũ (product.ImageUrl không đổi)

                    // Cập nhật các trường thông tin khác
                    product.Name = model.NameProduct;
                    product.CostPrice = model.OriginalPrice;
                    product.UnitPrice = model.Price;
                    product.UnitOfMeasure = model.Unit;
                    product.Description = model.Description ?? string.Empty;
                    product.IsActive = model.InStock == "true";
                    product.UpdatedAt = DateTime.UtcNow; // Cập nhật thời gian

                    // Cập nhật Category
                    var currentProductCategory = product.ProductCategorys.FirstOrDefault();
                    if (currentProductCategory == null || currentProductCategory.CategoryId != model.IdCategory)
                    {
                        // Xóa category cũ nếu có
                        if (currentProductCategory != null)
                        {
                            _context.ProductCategorys.Remove(currentProductCategory);
                        }
                        // Thêm category mới
                        var newProductCategory = new ProductCategory
                        {
                            ProductId = product.ProductId,
                            CategoryId = model.IdCategory
                        };
                        _context.ProductCategorys.Add(newProductCategory);
                    }
                    // Nếu category không đổi thì không cần làm gì

                    _context.Products.Update(product); // Đánh dấu Product là đã thay đổi
                    await _context.SaveChangesAsync(); // Lưu tất cả thay đổi (Product và ProductCategory)

                    await transaction.CommitAsync(); // Commit transaction DB

                    // Xóa ảnh cũ (NẾU có ảnh mới VÀ ảnh cũ tồn tại VÀ ảnh cũ khác ảnh mới)
                    if (!string.IsNullOrEmpty(newUniqueFileName) && !string.IsNullOrEmpty(oldImageUrl) && oldImageUrl != newUniqueFileName)
                    {
                        string oldFilePath = Path.Combine(_webHostEnvironment.WebRootPath, "product", oldImageUrl);
                        if (System.IO.File.Exists(oldFilePath))
                        {
                            try
                            {
                                System.IO.File.Delete(oldFilePath);
                            }
                            catch (IOException ioEx)
                            {
                                // Ghi log lỗi nhưng không làm dừng chương trình
                                Console.WriteLine($"Không thể xóa file ảnh cũ {oldFilePath}: {ioEx.Message}");
                            }
                        }
                    }

                    return Json(new { success = true, message = "Cập nhật sản phẩm thành công." });
                }
                catch (DbUpdateConcurrencyException) // Xử lý lỗi nếu dữ liệu đã bị thay đổi bởi người khác
                {
                    await transaction.RollbackAsync();
                    return StatusCode(409, new { success = false, message = "Dữ liệu đã bị thay đổi bởi người khác. Vui lòng tải lại trang và thử lại." });
                }
                catch (DbUpdateException dbEx)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine($"Lỗi DB khi cập nhật sản phẩm: {dbEx.InnerException?.Message ?? dbEx.Message}");
                    return Json(new { success = false, message = "Lỗi cơ sở dữ liệu khi cập nhật sản phẩm." });
                }
                catch (IOException ioEx)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine($"Lỗi IO khi lưu ảnh mới: {ioEx.Message}");
                    return Json(new { success = false, message = "Lỗi khi lưu hình ảnh mới." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine($"Lỗi chung khi cập nhật sản phẩm: {ex.ToString()}");
                    return Json(new { success = false, message = "Đã xảy ra lỗi không mong muốn khi cập nhật." });
                }
            }
            else // ModelState không hợp lệ
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors)
                                              .Select(e => e.ErrorMessage)
                                              .ToList();
                return Json(new { success = false, message = "Dữ liệu nhập không hợp lệ.", errors = errors });
            }
        }


        [HttpPost]
        public async Task<IActionResult> GetData([FromForm] DataTableRequest request)
        {
            // --- GIỮ NGUYÊN CODE GetData ---
            try
            {
                // --- Khởi tạo Query ---
                IQueryable<Product> query = _context.Products.AsNoTracking()
                                                .Include(p => p.ProductCategorys)
                                                .ThenInclude(pc => pc.Category)
                                                .AsQueryable();

                // --- Đếm tổng số bản ghi (TRƯỚC KHI LỌC) ---
                var recordsTotal = await query.CountAsync();

                // --- Lọc theo trạng thái (IsActive) ---
                var filterIsActiveValue = request.IsActive;
                if (!string.IsNullOrEmpty(filterIsActiveValue) && filterIsActiveValue.ToLowerInvariant() != "all")
                {
                    if (bool.TryParse(filterIsActiveValue, out bool isActive))
                    {
                        query = query.Where(p => p.IsActive == isActive);
                    }
                }

                // --- Lọc theo Khoảng Giá Bán (UnitPrice Range) ---
                if (request.UnitPriceFrom.HasValue && request.UnitPriceFrom > 0)
                {
                    query = query.Where(p => p.UnitPrice >= request.UnitPriceFrom.Value);
                }
                if (request.UnitPriceTo.HasValue && request.UnitPriceTo > 0 && request.UnitPriceTo >= (request.UnitPriceFrom ?? 0))
                {
                    query = query.Where(p => p.UnitPrice <= request.UnitPriceTo.Value);
                }

                // --- Lọc theo Khoảng Giá Vốn (CostPrice Range) (MỚI) ---
                if (request.CostPriceFrom.HasValue && request.CostPriceFrom > 0)
                {
                    query = query.Where(p => p.CostPrice >= request.CostPriceFrom.Value);
                }
                if (request.CostPriceTo.HasValue && request.CostPriceTo > 0 && request.CostPriceTo >= (request.CostPriceFrom ?? 0))
                {
                    query = query.Where(p => p.CostPrice <= request.CostPriceTo.Value);
                }

                // --- Lọc theo Khoảng Ngày tạo (CreatedDate Range) (MỚI) ---
                DateTime? fromDate = null;
                if (!string.IsNullOrEmpty(request.CreatedDateFrom) &&
                    DateTime.TryParseExact(request.CreatedDateFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFromDate))
                {
                    fromDate = parsedFromDate.Date; // Chỉ lấy phần ngày
                }

                DateTime? toDate = null;
                if (!string.IsNullOrEmpty(request.CreatedDateTo) &&
                    DateTime.TryParseExact(request.CreatedDateTo, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedToDate))
                {
                    toDate = parsedToDate.Date; // Chỉ lấy phần ngày
                }

                if (fromDate.HasValue)
                {
                    query = query.Where(p => p.CreatedAt.Date >= fromDate.Value);
                }
                if (toDate.HasValue)
                {
                    // Đảm bảo ngày đến >= ngày từ nếu cả hai đều có giá trị
                    if (!fromDate.HasValue || toDate.Value >= fromDate.Value)
                    {
                        query = query.Where(p => p.CreatedAt.Date <= toDate.Value);
                    }
                    // Optional: Nếu muốn xử lý trường hợp toDate < fromDate, bạn có thể bỏ qua bộ lọc toDate hoặc báo lỗi
                }


                // --- Lọc theo Danh sách Loại Sản phẩm (CategoryIds) ---
                if (request.CategoryIds != null && request.CategoryIds.Any(id => id > 0))
                {
                    var validCategoryIds = request.CategoryIds.Where(id => id > 0).ToList();
                    if (validCategoryIds.Any())
                    {
                        query = query.Where(p => p.ProductCategorys.Any(pc => validCategoryIds.Contains(pc.CategoryId)));
                    }
                }

                // --- Lọc theo Danh sách Đơn vị tính (UnitOfMeasures) ---
                if (request.UnitOfMeasures != null && request.UnitOfMeasures.Any())
                {
                    var validUnits = request.UnitOfMeasures
                                            .Where(u => !string.IsNullOrWhiteSpace(u))
                                            .Select(u => u.Trim().ToLowerInvariant()) // Chuyển về lowercase ở C#
                                            .Distinct()
                                            .ToList();
                    if (validUnits.Any())
                    {
                        // Sử dụng ToLower() trong LINQ to Entities (có thể được dịch tốt hơn)
                        query = query.Where(p => p.UnitOfMeasure != null && validUnits.Contains(p.UnitOfMeasure.ToLower()));
                    }
                }

                // --- Tìm kiếm ---
                var searchValue = request.Search?.Value?.Trim();
                var searchLower = searchValue?.ToLowerInvariant();

                if (!string.IsNullOrEmpty(searchValue) && !string.IsNullOrEmpty(searchLower))
                {
                    DateTime? searchDate = null;
                    if (DateTime.TryParseExact(searchValue, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateResult))
                    {
                        searchDate = dateResult.Date;
                    }

                    decimal? searchNumeric = null;
                    // Chuẩn hóa chuỗi số: bỏ dấu phân cách ngàn, đổi dấu thập phân nếu cần
                    // Giả định input là kiểu Việt Nam (dấu phẩy cho thập phân, chấm cho ngàn - nhưng user có thể nhập lẫn lộn)
                    // Bỏ hết dấu chấm -> đổi dấu phẩy thành chấm -> parse
                    string cleanedNumericString = searchValue.Replace(".", "").Replace(",", ".");
                    if (decimal.TryParse(cleanedNumericString, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericResult))
                    {
                        searchNumeric = numericResult;
                        // Xử lý trường hợp nhập số lớn không có dấu phân cách thập phân nhưng có thể có dấu ngàn kiểu US
                        if (!cleanedNumericString.Contains('.') && decimal.TryParse(searchValue.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var largeNumericResultUS))
                        {
                            // Nếu parse thành công kiểu US và khác kết quả kiểu VN, có thể ưu tiên kiểu US nếu số lớn?
                            // Hoặc đơn giản là chấp nhận kết quả parse đầu tiên thành công
                            // searchNumeric = largeNumericResultUS; // Cẩn thận logic này
                        }
                    }
                    else
                    {
                        // Thử parse trực tiếp nếu kiểu US (chấm thập phân)
                        if (decimal.TryParse(searchValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericResultUS))
                        {
                            searchNumeric = numericResultUS;
                        }
                    }


                    query = query.Where(p =>
                        (p.Name != null && p.Name.ToLower().Contains(searchLower)) ||
                        (p.UnitOfMeasure != null && p.UnitOfMeasure.ToLower().Contains(searchLower)) ||
                        p.ProductCategorys.Any(pc => pc.Category != null && pc.Category.NameCategory != null && pc.Category.NameCategory.ToLower().Contains(searchLower)) ||
                        (searchNumeric.HasValue && (p.UnitPrice == searchNumeric.Value || p.CostPrice == searchNumeric.Value)) || // Thêm CostPrice vào search
                        (searchDate.HasValue && p.CreatedAt.Date == searchDate.Value)
                    );
                }

                // --- Đếm số bản ghi sau khi lọc (TRƯỚC KHI PHÂN TRANG) ---
                var recordsFiltered = await query.CountAsync();

                // --- Sắp xếp ---
                var sortColumn = "";
                var sortDirection = "desc"; // Mặc định giảm dần theo ngày tạo

                if (request.Order != null && request.Order.Any())
                {
                    var firstOrder = request.Order.First();
                    var columnIndex = firstOrder.Column;
                    sortDirection = firstOrder.Dir?.ToLowerInvariant() ?? "asc";

                    if (request.Columns != null && columnIndex >= 0 && columnIndex < request.Columns.Count)
                    {
                        sortColumn = request.Columns[columnIndex].Data?.ToLowerInvariant() ?? "";
                    }
                }

                // Áp dụng sắp xếp
                switch (sortColumn)
                {
                    case "nameproduct":
                        query = sortDirection == "asc" ? query.OrderBy(p => p.Name) : query.OrderByDescending(p => p.Name);
                        break;
                    case "category":
                        query = sortDirection == "asc"
                                ? query.OrderBy(p => p.ProductCategorys.Select(pc => pc.Category.NameCategory).FirstOrDefault())
                                : query.OrderByDescending(p => p.ProductCategorys.Select(pc => pc.Category.NameCategory).FirstOrDefault());
                        break;
                    case "unitofmeasure":
                        query = sortDirection == "asc" ? query.OrderBy(p => p.UnitOfMeasure) : query.OrderByDescending(p => p.UnitOfMeasure);
                        break;
                    case "unitprice":
                        query = sortDirection == "asc" ? query.OrderBy(p => p.UnitPrice) : query.OrderByDescending(p => p.UnitPrice);
                        break;
                    case "costprice":
                        query = sortDirection == "asc" ? query.OrderBy(p => p.CostPrice) : query.OrderByDescending(p => p.CostPrice);
                        break;
                    case "isactive":
                        query = sortDirection == "asc" ? query.OrderBy(p => p.IsActive) : query.OrderByDescending(p => p.IsActive);
                        break;
                    case "createdat": // Rõ ràng xử lý cột ngày tạo
                    default: // Mặc định hoặc nếu cột không khớp
                        query = sortDirection == "asc" ? query.OrderBy(p => p.CreatedAt) : query.OrderByDescending(p => p.CreatedAt);
                        break;
                }

                // Thêm sắp xếp phụ theo ID để đảm bảo thứ tự ổn định
                if (query is IOrderedQueryable<Product> orderedQuery) // Chỉ thêm ThenBy nếu đã có OrderBy
                {
                    // Thêm ThenBy theo ProductId tăng dần làm khóa phụ cuối cùng
                    query = orderedQuery.ThenBy(p => p.ProductId);
                }
                else
                {
                    // Trường hợp mặc định nếu chưa có sắp xếp nào được áp dụng (ít xảy ra)
                    query = query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.ProductId);
                }


                // --- Phân trang ---
                var items = await query
                                .Skip(request.Start)
                                .Take(request.Length > 0 ? request.Length : 10) // Mặc định 10 nếu Length <= 0
                                .ToListAsync();

                // --- Định dạng dữ liệu trả về ---
                var responseData = items.Select(p => new
                {
                    p.ProductId, // ID gốc để dùng cho edit/delete
                    ImageProduct = p.ImageUrl ?? "default-product.png", // Vẫn dùng ImageUrl
                    NameProduct = p.Name, // Đổi tên cho khớp JS
                    UnitOfMeasure = p.UnitOfMeasure ?? "N/A", // Đổi tên cho khớp JS
                    Category = p.ProductCategorys.Select(pc => pc.Category.NameCategory).FirstOrDefault() ?? "N/A",
                    UnitPrice = p.UnitPrice, // Đổi tên cho khớp JS
                    CostPrice = p.CostPrice, // Trả về giá vốn
                    CreatedAt = p.CreatedAt, // Trả về DateTime (UTC)
                    Id = p.ProductId, // Giữ lại Id nếu JS cũ có dùng
                    IsActive = p.IsActive,
                    DT_RowId = $"row_{p.ProductId}" // Cung cấp ID cho hàng của DataTable
                }).ToList();

                // --- Trả về JSON cho DataTables ---
                return Json(new
                {
                    draw = request.Draw,
                    recordsTotal = recordsTotal,
                    recordsFiltered = recordsFiltered,
                    data = responseData
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi trong GetData: {ex.ToString()}");
                return Json(new
                {
                    draw = request.Draw,
                    recordsTotal = 0,
                    recordsFiltered = 0,
                    data = new List<object>(),
                    error = "Đã xảy ra lỗi máy chủ khi tải dữ liệu."
                });
            }
        }

        // Action GetCategories (GIỮ NGUYÊN)
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            // --- GIỮ NGUYÊN CODE GetCategories ---
            try
            {
                var categories = await _context.Categories
                                            .AsNoTracking()
                                            .Where(c => c.IsActive) // Chỉ lấy loại đang hoạt động
                                            .Select(c => new { c.Id, c.NameCategory })
                                            .OrderBy(c => c.NameCategory)
                                            .ToListAsync();
                return Json(categories);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi GetCategories: {ex.Message}");
                return Json(new List<object>()); // Trả về danh sách rỗng nếu lỗi
            }
        }

        // Action GetUnits (GIỮ NGUYÊN)
        [HttpGet]
        public async Task<IActionResult> GetUnits()
        {
            // --- GIỮ NGUYÊN CODE GetUnits ---
            try
            {

                // Lấy các tên từ bảng UnitOfMeasures (nếu có và muốn kết hợp)
                var unitsFromTable = await _context.UnitOfMeasures
                                          .AsNoTracking()
                                          .Where(u => u.IsActive) // Optional filter
                                          .Select(u => u.NameUnitOfMeasure)
                                          .Where(name => !string.IsNullOrEmpty(name))
                                          .ToListAsync();

                // Kết hợp và loại bỏ trùng lặp (ví dụ)
                var combinedUnits = unitsFromTable.Union(unitsFromTable, StringComparer.OrdinalIgnoreCase)
                                                     .OrderBy(u => u)
                                                     .ToList();

                // Hiện tại chỉ trả về từ bảng Products để đảm bảo khớp dữ liệu hiện có
                return Json(combinedUnits);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi GetUnits: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                return Json(new List<string>()); // Trả về danh sách rỗng nếu lỗi
            }
        }


        // Action Delete (GIỮ NGUYÊN)
        [HttpPost]
        //[ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            // --- GIỮ NGUYÊN CODE DELETE ---
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var product = await _context.Products
                    .Include(p => p.ProductCategorys) // Include để xóa cả ProductCategory
                    .FirstOrDefaultAsync(p => p.ProductId == id);

                if (product == null)
                {
                    await transaction.RollbackAsync();
                    return Json(new { success = false, message = "Không tìm thấy sản phẩm để xóa." });
                }


                // Xóa các bản ghi liên quan trong ProductCategory
                if (product.ProductCategorys != null && product.ProductCategorys.Any())
                {
                    _context.ProductCategorys.RemoveRange(product.ProductCategorys);
                }

                string? imageToDelete = product.ImageUrl;

                // Xóa bản ghi Product
                _context.Products.Remove(product);

                await _context.SaveChangesAsync(); // Lưu thay đổi vào DB

                await transaction.CommitAsync(); // Commit transaction DB

                // Xóa file ảnh (sau khi DB đã commit)
                if (!string.IsNullOrEmpty(imageToDelete))
                {
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "product");
                    string filePath = Path.Combine(uploadsFolder, imageToDelete);
                    if (System.IO.File.Exists(filePath))
                    {
                        try { System.IO.File.Delete(filePath); }
                        catch (IOException ioEx)
                        {
                            Console.WriteLine($"Không thể xóa file ảnh {filePath}: {ioEx.Message}");
                            // Ghi log nhưng không báo lỗi nghiêm trọng cho người dùng
                        }
                    }
                }

                return Json(new { success = true, message = "Sản phẩm đã được xóa thành công." });
            }
            catch (DbUpdateException dbEx) // Bắt lỗi constraint DB khác (ít xảy ra hơn nếu đã check OrderDetails)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Lỗi DB khi xóa sản phẩm: {dbEx.InnerException?.Message ?? dbEx.Message}");
                return Json(new { success = false, message = "Lỗi cơ sở dữ liệu khi xóa. Sản phẩm có thể đang được tham chiếu ở nơi khác." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Lỗi chung khi xóa sản phẩm: {ex.ToString()}");
                return Json(new { success = false, message = "Đã xảy ra lỗi không mong muốn khi xóa sản phẩm." });
            }
        }
    }
}