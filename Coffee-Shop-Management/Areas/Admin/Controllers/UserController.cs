using Coffee_Shop_Management.Models;
using Coffee_Shop_Management.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Dynamic.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Collections.Generic; // Needed for List<string> in ViewModel
using static Coffee_Shop_Management.Models.AppDbContext;
using AspNetCoreHero.ToastNotification.Abstractions;
using Microsoft.AspNetCore.Authorization; // Keep if needed

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    // [Authorize(Roles = "Admin")] // <-- Consider adding authorization
    public class UserController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IWebHostEnvironment _webHostEnvironment; // <-- THÊM: Để upload file
        private readonly INotyfService _notification;


        // Nên sử dụng ILogger trong ứng dụng thực tế
        // private readonly ILogger<UserController> _logger;

        public UserController(
            AppDbContext context,
            UserManager<AppUser> userManager,
            RoleManager<IdentityRole> roleManager,
            SignInManager<AppUser> signInManager,
            IWebHostEnvironment webHostEnvironment,
             INotyfService notification
            /*, ILogger<UserController> logger */)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _webHostEnvironment = webHostEnvironment;
            _notification = notification;
            _signInManager = signInManager;
            // _logger = logger;
        }

        // GET: Admin/User
        public IActionResult Index()
        {
            return View();
        }

        // ===== THÊM ACTION NÀY =====
        [HttpGet]
        public async Task<IActionResult> GetJobTitles()
        {
            try
            {
                var jobTitles = await _userManager.Users // Hoặc _context.Users
                                            .Where(u => !u.DeleteTemp && !string.IsNullOrEmpty(u.JobTitle))
                                            .Select(u => u.JobTitle)
                                            .Distinct()
                                            .OrderBy(jt => jt) // Sắp xếp A-Z
                                            .ToListAsync();
                // Đảm bảo trả về một danh sách các chuỗi (string)
                return Json(new { success = true, data = jobTitles });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching job titles: {ex.Message}");
                return Json(new { success = false, message = "Lỗi khi tải danh sách chức vụ." });
            }
        }

        // POST: Admin/User/GetDataActive
        [HttpPost]
        // No changes needed here, this is for DataTables AJAX, not form submission
        public async Task<IActionResult> GetDataActive()
        {
            try
            {
                var draw = Request.Form["draw"].FirstOrDefault();
                var start = Request.Form["start"].FirstOrDefault();
                var length = Request.Form["length"].FirstOrDefault();
                var orderColumnIndex = Request.Form["order[0][column]"].FirstOrDefault();
                var sortColumn = Request.Form[$"columns[{orderColumnIndex}][name]"].FirstOrDefault();
                var sortColumnDirection = Request.Form["order[0][dir]"].FirstOrDefault();
                var searchValue = Request.Form["searchValue"].FirstOrDefault();
                var statusFilter = Request.Form["statusFilter"].FirstOrDefault();
                var jobTitleFilter = Request.Form["jobTitleFilter"].FirstOrDefault();

                int pageSize = length != null ? Convert.ToInt32(length) : 10;
                int skip = start != null ? Convert.ToInt32(start) : 0;

                var userQuery = _userManager.Users.Where(u => !u.DeleteTemp);
                int recordsTotal = await userQuery.CountAsync();

                if (!string.IsNullOrEmpty(searchValue))
                {
                    var searchLower = searchValue.ToLower();
                    userQuery = userQuery.Where(u =>
                        (u.Name != null && u.Name.ToLower().Contains(searchLower)) ||
                        (u.UserName != null && u.UserName.ToLower().Contains(searchLower)) ||
                        (u.Email != null && u.Email.ToLower().Contains(searchLower)) ||
                        (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(searchLower))
                    );
                }

                if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "all")
                {
                    if (bool.TryParse(statusFilter, out bool isLocked))
                    {
                        if (isLocked) userQuery = userQuery.Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow);
                        else userQuery = userQuery.Where(u => u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow);
                    }
                }

                if (!string.IsNullOrEmpty(jobTitleFilter) && jobTitleFilter != "all")
                {
                    // So sánh chính xác chuỗi chức vụ
                    userQuery = userQuery.Where(u => u.JobTitle == jobTitleFilter);
                }

                int recordsFiltered = await userQuery.CountAsync();

                string defaultSortColumn = "CreatedAt";
                string defaultSortDirection = "desc";
                string finalSortColumn = string.IsNullOrEmpty(sortColumn) ? defaultSortColumn : sortColumn;
                string finalSortDirection = string.IsNullOrEmpty(sortColumnDirection) ? defaultSortDirection : sortColumnDirection;
                var validColumns = new[] { "Name", "UserName", "Email", "PhoneNumber", "JobTitle", "LockoutEnabled", "LockoutEnd", "CreatedAt", "UpdateAt" };

                if (!validColumns.Contains(finalSortColumn))
                {
                    finalSortColumn = defaultSortColumn;
                    finalSortDirection = defaultSortDirection;
                }

                try
                {
                    userQuery = userQuery.OrderBy($"{finalSortColumn} {finalSortDirection}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sorting active users: {ex.Message}");
                    userQuery = userQuery.OrderByDescending(u => u.CreatedAt);
                }

                var data = await userQuery.Skip(skip).Take(pageSize).ToListAsync();
                var responseData = data.Select(u => new
                {
                    u.Id,
                    u.Name,
                    u.UserName,
                    u.Email,
                    u.PhoneNumber,
                    u.JobTitle,
                    u.ImageUrl,
                    IsLocked = u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow,
                    u.CreatedAt,
                    u.UpdateAt
                }).ToList();

                return Ok(new { draw, recordsFiltered, recordsTotal, data = responseData });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDataActive (User): {ex.Message}");
                return StatusCode(500, new { error = "Lỗi máy chủ nội bộ khi tải danh sách người dùng." });
            }
        }

        // POST: Admin/User/GetDataDeleted
        [HttpPost]
        // No changes needed here
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
                var searchValue = Request.Form["searchValue"].FirstOrDefault();

                int pageSize = length != null ? Convert.ToInt32(length) : 10;
                int skip = start != null ? Convert.ToInt32(start) : 0;

                // Use _context for easier DeleteTemp filtering
                var userQuery = _context.Users.Where(u => u.DeleteTemp);
                int recordsTotal = await userQuery.CountAsync();

                if (!string.IsNullOrEmpty(searchValue))
                {
                    var searchLower = searchValue.ToLower();
                    userQuery = userQuery.Where(u =>
                      (u.Name != null && u.Name.ToLower().Contains(searchLower)) ||
                      (u.UserName != null && u.UserName.ToLower().Contains(searchLower)) ||
                      (u.Email != null && u.Email.ToLower().Contains(searchLower)) ||
                      (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(searchLower))
                   );
                }

                int recordsFiltered = await userQuery.CountAsync();

                string defaultSortColumn = "UpdateAt";
                string defaultSortDirection = "desc";
                string finalSortColumn = string.IsNullOrEmpty(sortColumn) ? defaultSortColumn : sortColumn;
                string finalSortDirection = string.IsNullOrEmpty(sortColumnDirection) ? defaultSortDirection : sortColumnDirection;
                var validColumns = new[] { "Name", "UserName", "Email", "PhoneNumber", "JobTitle", "CreatedAt", "UpdateAt" };

                if (!validColumns.Contains(finalSortColumn))
                {
                    finalSortColumn = defaultSortColumn;
                    finalSortDirection = defaultSortDirection;
                }

                try
                {
                    userQuery = userQuery.OrderBy($"{finalSortColumn} {finalSortDirection}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sorting deleted users: {ex.Message}");
                    userQuery = userQuery.OrderByDescending(u => u.UpdateAt);
                }

                var data = await userQuery.Skip(skip).Take(pageSize).ToListAsync();
                var responseData = data.Select(u => new
                {
                    u.Id,
                    u.Name,
                    u.UserName,
                    u.Email,
                    u.PhoneNumber,
                    u.ImageUrl,
                    u.CreatedAt,
                    u.UpdateAt
                }).ToList();

                return Ok(new { draw, recordsFiltered, recordsTotal, data = responseData });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetDataDeleted (User): {ex.Message}");
                return StatusCode(500, new { error = "Lỗi máy chủ nội bộ khi tải danh sách người dùng đã xóa." });
            }
        }

        //Lấy tất cả các vai trò
        [HttpGet]
        public async Task<IActionResult> GetAllRoles()
        {
            try
            {
                // Lấy danh sách tên các vai trò, sắp xếp theo tên
                var roleNames = await _roleManager.Roles
                                            .OrderBy(r => r.Name)
                                            .Select(r => r.Name) // Chỉ lấy tên
                                            .ToListAsync();
                // Trả về JSON chứa danh sách tên vai trò
                return Json(new { success = true, roles = roleNames });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching all roles: {ex.Message}");
                // Log lỗi ở đây nếu cần (ví dụ: _logger.LogError...)
                return Json(new { success = false, message = "Lỗi khi tải danh sách vai trò." });
            }
        }

        // GET: Admin/User/GetUserDetails/{id}
        [HttpGet]
        // No changes needed here
        public async Task<IActionResult> GetUserDetails(string id)
        {
            if (string.IsNullOrEmpty(id)) return BadRequest(new { message = "ID người dùng không được trống." });

            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == id && !u.DeleteTemp);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng hoặc người dùng đã bị xóa." });

            var userRoles = await _userManager.GetRolesAsync(user);
            var allRoles = await _roleManager.Roles.OrderBy(r => r.Name).Select(r => r.Name).ToListAsync();

            var viewModel = new UserViewModel
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Name = user.Name,
                PhoneNumber = user.PhoneNumber,
                Address = user.Address,
                JobTitle = user.JobTitle,
                BirthDate = user.BirthDate,
                WageRate = user.WageRate,
                ImageUrl = user.ImageUrl,
                IsLocked = user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow,
                CreatedAt = user.CreatedAt,
                SelectedRoles = userRoles.ToList(),
                AllRoles = allRoles
            };
            return Json(viewModel);
        }

        // POST: Admin/User/Create
        [HttpPost]
        [ValidateAntiForgeryToken] // Automatically validates token from form data
        // Sử dụng [FromForm] để bind dữ liệu từ form, bao gồm cả file
        public async Task<IActionResult> Create([FromForm] UserViewModel model)
        {
            // Bỏ check Password nếu không bắt buộc ngay từ đầu trong ViewModel
            ModelState.Remove(nameof(model.Password));
            ModelState.Remove(nameof(model.ConfirmPassword));

            // Validate Password và ConfirmPassword chỉ khi tạo mới
            if (string.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.AddModelError(nameof(model.Password), "Mật khẩu là bắt buộc khi tạo mới.");
            }
            else if (model.Password.Length < 6)
            {
                ModelState.AddModelError(nameof(model.Password), "Mật khẩu phải có ít nhất 6 ký tự.");
            }
            if (model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError(nameof(model.ConfirmPassword), "Mật khẩu xác nhận không khớp.");
            }


            if (ModelState.IsValid)
            {
                bool userNameExists = await _userManager.Users.AnyAsync(u => !u.DeleteTemp && u.UserName == model.UserName);
                if (userNameExists) return Json(new { success = false, message = "Tên đăng nhập đã tồn tại." });

                bool emailExists = await _userManager.Users.AnyAsync(u => !u.DeleteTemp && u.Email == model.Email);
                if (emailExists) return Json(new { success = false, message = "Email đã tồn tại." });

                string? uniqueFileName = null;
                if (model.AvatarFile != null)
                {
                    uniqueFileName = await UploadAvatar(model.AvatarFile);
                    if (string.IsNullOrEmpty(uniqueFileName))
                        return Json(new { success = false, message = "Lỗi khi upload ảnh đại diện." });
                }

                var user = new AppUser
                {
                    UserName = model.UserName,
                    Email = model.Email,
                    Name = model.Name,
                    PhoneNumber = model.PhoneNumber,
                    Address = model.Address ?? "",
                    JobTitle = model.JobTitle,
                    BirthDate = model.BirthDate,
                    WageRate = model.WageRate,
                    ImageUrl = uniqueFileName ?? "",
                    // Xử lý Lockout dựa trên IsLocked
                    LockoutEnabled = true, // Nên bật để có thể khóa
                    LockoutEnd = model.IsLocked ? DateTimeOffset.MaxValue : (DateTimeOffset?)null,
                    EmailConfirmed = true, // Tạm thời xác nhận luôn
                    CreatedAt = DateTime.Now,
                    UpdateAt = DateTime.Now,
                    DeleteTemp = false
                };

                // CreateAsync sẽ hash mật khẩu
                var result = await _userManager.CreateAsync(user, model.Password!); // Use ! as we validated non-null above

                if (result.Succeeded)
                {
                    if (model.SelectedRoles != null && model.SelectedRoles.Any())
                    {
                        var roleResult = await _userManager.AddToRolesAsync(user, model.SelectedRoles.Distinct());
                        if (!roleResult.Succeeded)
                        {
                            Console.WriteLine($"Warning: Could not add roles for user {user.UserName}. Errors: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
                        }
                    }
                    return Json(new { success = true, message = "Thêm người dùng thành công!" });
                }
                else
                {
                    if (!string.IsNullOrEmpty(uniqueFileName)) DeleteAvatar(uniqueFileName); // Xóa ảnh nếu tạo user lỗi
                    var errors = result.Errors.Select(e => e.Description).ToList();
                    return Json(new { success = false, message = "Lỗi khi tạo người dùng.", errors });
                }
            }

            var modelErrors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            return Json(new { success = false, message = "Dữ liệu không hợp lệ.", errors = modelErrors });
        }


        // POST: Admin/User/Edit
        [HttpPost]
        [ValidateAntiForgeryToken] // Automatically validates token from form data
        // Sử dụng [FromForm] để bind dữ liệu từ form, bao gồm cả file và Id ẩn
        public async Task<IActionResult> Edit([FromForm] UserViewModel model)
        {
            // Id được bind từ hidden input trong form
            if (string.IsNullOrEmpty(model.Id)) return BadRequest(new { message = "ID người dùng không hợp lệ." });

            // Bỏ validate Password/ConfirmPassword vì chúng không bắt buộc khi Edit
            ModelState.Remove(nameof(model.Password));
            ModelState.Remove(nameof(model.ConfirmPassword));
            // Validate password chỉ khi nó được cung cấp
            if (!string.IsNullOrEmpty(model.Password))
            {
                if (model.Password.Length < 6)
                {
                    ModelState.AddModelError(nameof(model.Password), "Mật khẩu mới phải có ít nhất 6 ký tự.");
                }
                if (model.Password != model.ConfirmPassword)
                {
                    ModelState.AddModelError(nameof(model.ConfirmPassword), "Mật khẩu xác nhận không khớp.");
                }
            }


            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByIdAsync(model.Id);
                if (user == null || user.DeleteTemp)
                    return NotFound(new { message = "Không tìm thấy người dùng để cập nhật hoặc người dùng đã bị xóa." });

                // Kiểm tra trùng UserName (nếu thay đổi)
                if (user.UserName != model.UserName)
                {
                    bool userNameExists = await _userManager.Users.AnyAsync(u => !u.DeleteTemp && u.Id != model.Id && u.UserName == model.UserName);
                    if (userNameExists) return Json(new { success = false, message = "Tên đăng nhập đã tồn tại." });
                }
                // Kiểm tra trùng Email (nếu thay đổi)
                if (user.Email != model.Email)
                {
                    bool emailExists = await _userManager.Users.AnyAsync(u => !u.DeleteTemp && u.Id != model.Id && u.Email == model.Email);
                    if (emailExists) return Json(new { success = false, message = "Email đã tồn tại." });
                }


                string? uniqueFileName = user.ImageUrl;
                string oldImageUrl = user.ImageUrl;
                if (model.AvatarFile != null)
                {
                    uniqueFileName = await UploadAvatar(model.AvatarFile);
                    if (string.IsNullOrEmpty(uniqueFileName))
                        return Json(new { success = false, message = "Lỗi khi upload ảnh đại diện mới." });
                    user.ImageUrl = uniqueFileName; // Cập nhật ảnh mới
                }

                // Cập nhật các thuộc tính khác
                user.UserName = model.UserName;
                user.Email = model.Email;
                user.Name = model.Name;
                user.PhoneNumber = model.PhoneNumber;
                user.Address = model.Address ?? "";
                user.JobTitle = model.JobTitle;
                user.BirthDate = model.BirthDate;
                user.WageRate = model.WageRate;
                user.UpdateAt = DateTime.Now;
                // Cập nhật LockoutEnd dựa trên IsLocked
                if (model.IsLocked && (user.LockoutEnd == null || user.LockoutEnd <= DateTimeOffset.UtcNow))
                {
                    user.LockoutEnd = DateTimeOffset.MaxValue;
                }
                else if (!model.IsLocked && user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow)
                {
                    user.LockoutEnd = DateTimeOffset.UtcNow; // or null
                }

                var updateResult = await _userManager.UpdateAsync(user);

                if (updateResult.Succeeded)
                {
                    // Xóa ảnh cũ nếu upload ảnh mới thành công
                    if (model.AvatarFile != null && !string.IsNullOrEmpty(oldImageUrl) && oldImageUrl != uniqueFileName)
                    {
                        DeleteAvatar(oldImageUrl);
                    }

                    // Cập nhật mật khẩu nếu có nhập
                    if (!string.IsNullOrEmpty(model.Password))
                    {
                        // Cần RemovePassword trước khi AddPassword mới
                        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                        var changePasswordResult = await _userManager.ResetPasswordAsync(user, token, model.Password);

                        if (!changePasswordResult.Succeeded)
                        {
                            Console.WriteLine($"Error: Could not change password for user {user.UserName}. Errors: {string.Join(", ", changePasswordResult.Errors.Select(e => e.Description))}");
                            // Cân nhắc trả về lỗi hoặc tiếp tục với thông báo thành công (nhưng pass không đổi)
                            // return Json(new { success = false, message = "Cập nhật thông tin thành công nhưng lỗi đổi mật khẩu.", errors = changePasswordResult.Errors.Select(e => e.Description).ToList() });
                        }
                    }

                    // Cập nhật Roles
                    var currentRoles = await _userManager.GetRolesAsync(user);
                    var selectedRoles = model.SelectedRoles ?? new List<string>();
                    var rolesToRemove = currentRoles.Except(selectedRoles).ToArray();
                    var rolesToAdd = selectedRoles.Except(currentRoles).ToArray();

                    IdentityResult roleRemoveResult = rolesToRemove.Any() ? await _userManager.RemoveFromRolesAsync(user, rolesToRemove) : IdentityResult.Success;
                    IdentityResult roleAddResult = (roleRemoveResult.Succeeded && rolesToAdd.Any()) ? await _userManager.AddToRolesAsync(user, rolesToAdd) : IdentityResult.Success;

                    if (!roleRemoveResult.Succeeded || !roleAddResult.Succeeded)
                    {
                        Console.WriteLine($"Warning: Role update issue for user {user.UserName}. RemoveErrors: {string.Join(",", roleRemoveResult.Errors.Select(e => e.Description))}, AddErrors: {string.Join(",", roleAddResult.Errors.Select(e => e.Description))}");
                        // Có thể trả về lỗi hoặc chỉ log cảnh báo
                    }


                    return Json(new { success = true, message = "Cập nhật người dùng thành công!" });
                }
                else
                {
                    // Nếu user update thất bại, xóa file ảnh mới upload (nếu có và khác ảnh cũ)
                    if (model.AvatarFile != null && uniqueFileName != oldImageUrl)
                    {
                        DeleteAvatar(uniqueFileName);
                    }
                    var errors = updateResult.Errors.Select(e => e.Description).ToList();
                    return Json(new { success = false, message = "Lỗi khi cập nhật người dùng.", errors });
                }
            }

            var modelErrors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            return Json(new { success = false, message = "Dữ liệu không hợp lệ.", errors = modelErrors });
        }


        // POST: Admin/User/Delete/{id}
        [HttpPost]
        [ValidateAntiForgeryToken] // Token vẫn cần được gửi (AJAX hiện tại đã làm)
        // Không dùng [FromForm] ở đây
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id)) return Json(new { success = false, message = "ID người dùng không hợp lệ." });
            var currentUserId = _userManager.GetUserId(User);
            if (id == currentUserId) return Json(new { success = false, message = "Bạn không thể tự xóa tài khoản của mình." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id && !u.DeleteTemp);
            if (user == null) return Json(new { success = true, message = "Người dùng không tồn tại hoặc đã bị xóa." });

            try
            {
                user.DeleteTemp = true;
                user.UpdateAt = DateTime.Now;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Xóa người dùng thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Soft Deleting User: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi xóa dữ liệu người dùng." });
            }
        }

        // POST: Admin/User/Restore/{id}
        [HttpPost]
        [ValidateAntiForgeryToken] // Token vẫn cần được gửi (AJAX hiện tại đã làm)
        // Không dùng [FromForm] ở đây
        public async Task<IActionResult> Restore(string id)
        {
            if (string.IsNullOrEmpty(id)) return Json(new { success = false, message = "ID người dùng không hợp lệ." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id && u.DeleteTemp);
            if (user == null) return Json(new { success = false, message = "Không tìm thấy người dùng đã xóa." });

            bool userNameExists = await _context.Users.AnyAsync(u => !u.DeleteTemp && u.UserName == user.UserName);
            if (userNameExists) return Json(new { success = false, message = $"Không thể khôi phục. Tên đăng nhập '{user.UserName}' đã tồn tại." });

            bool emailExists = await _context.Users.AnyAsync(u => !u.DeleteTemp && u.Email == user.Email);
            if (emailExists) return Json(new { success = false, message = $"Không thể khôi phục. Email '{user.Email}' đã tồn tại." });

            try
            {
                user.DeleteTemp = false;
                user.UpdateAt = DateTime.Now;
                _context.Users.Update(user);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Khôi phục người dùng thành công!" });
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"Error Restoring User: {ex.InnerException?.Message ?? ex.Message}");
                return Json(new { success = false, message = "Đã xảy ra lỗi khi khôi phục dữ liệu người dùng." });
            }
        }


        // =========================================================================
        // HELPER METHODS (UploadAvatar, DeleteAvatar) - Không thay đổi
        // =========================================================================
        private async Task<string?> UploadAvatar(IFormFile avatarFile)
        {
            if (avatarFile == null || avatarFile.Length == 0) return null;

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var extension = Path.GetExtension(avatarFile.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                Console.WriteLine($"Upload Error: Invalid file type '{extension}'"); return null;
            }

            long maxFileSize = 5 * 1024 * 1024; // 5MB
            if (avatarFile.Length > maxFileSize)
            {
                Console.WriteLine($"Upload Error: File size too large '{avatarFile.Length / 1024} KB'"); return null;
            }

            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "avatars");
            if (!Directory.Exists(uploadsFolder))
            {
                try { Directory.CreateDirectory(uploadsFolder); }
                catch (Exception ex) { Console.WriteLine($"Upload Error: Cannot create directory '{uploadsFolder}'. Error: {ex.Message}"); return null; }
            }

            string uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(avatarFile.FileName);
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create)) { await avatarFile.CopyToAsync(fileStream); }
                return Path.Combine("/uploads/avatars/", uniqueFileName).Replace("\\", "/"); // Relative path
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Upload Error: Could not save file '{filePath}'. Error: {ex.Message}");
                if (System.IO.File.Exists(filePath)) { try { System.IO.File.Delete(filePath); } catch { } }
                return null;
            }
        }

        private void DeleteAvatar(string? relativeImagePath)
        {
            if (string.IsNullOrEmpty(relativeImagePath)) return;
            try
            {
                var fileName = Path.GetFileName(relativeImagePath);
                if (string.IsNullOrEmpty(fileName)) return;
                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "avatars");
                string filePath = Path.Combine(uploadsFolder, fileName);
                if (System.IO.File.Exists(filePath)) { System.IO.File.Delete(filePath); Console.WriteLine($"Deleted old avatar: {filePath}"); }
            }
            catch (Exception ex) { Console.WriteLine($"Error deleting old avatar '{relativeImagePath}': {ex.Message}"); }
        }

        [HttpGet("Login")]
        public IActionResult Login()
        {
            if (!HttpContext.User.Identity!.IsAuthenticated)
            {
                return View(new LoginVM());
            }
            return RedirectToAction("Index", "Product", new { area = "Admin" });
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginVM loginVM)
        {
            if (!ModelState.IsValid)
            {
                return View(loginVM);
            }

            // Sử dụng PasswordSignInAsync để đăng nhập.  Tham số thứ tư (lockoutOnFailure) phải là true.
            var result = await _signInManager.PasswordSignInAsync(loginVM.Username, loginVM.Password, loginVM.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                _notification.Success("Đăng nhập thành công!");
                return RedirectToAction("Index", "Product", new { area = "Admin" });
            }

            if (result.IsLockedOut)
            {
                // Tài khoản đã bị khóa
                _notification.Error("Tài khoản của bạn đã bị khóa. Vui lòng thử lại sau.");  // Hoặc chuyển hướng đến trang thông báo khóa
                                                                                             // Hoặc có thể gửi email thông báo cho người dùng
                return View(loginVM); // Hoặc return View("Lockout"); nếu bạn có view riêng cho trang khóa
            }

            //if (result.IsNotAllowed)
            //{
            //    // Tài khoản chưa được xác nhận (ví dụ: email chưa confirm)
            //    _notification.Error("Tài khoản chưa được xác nhận email");
            //    return View(loginVM);
            //}

            // Các trường hợp còn lại (ví dụ: sai mật khẩu, nhưng chưa bị khóa)
            _notification.Error("Sai tên đăng nhập hoặc mật khẩu");


            ModelState.AddModelError(string.Empty, "Invalid login attempt."); // Thêm lỗi chung vào ModelState (tùy chọn)
            return View(loginVM);
        }

        [HttpPost]
        [Authorize]
        public IActionResult Logout()
        {
            _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home", new { area = "" });
        }

        [HttpGet("AccessDenied")]
        [Authorize]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}