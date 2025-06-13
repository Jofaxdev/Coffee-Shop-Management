using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Identity;
using static Coffee_Shop_Management.Models.AppDbContext;

namespace Coffee_Shop_Management.Utilites
{
    public class DbInitializer : IDbInitializer
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        public DbInitializer(AppDbContext context,
                               UserManager<AppUser> userManager,
                               RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task InitializeAsync()
        {
            try // Bọc trong try-catch để xử lý lỗi tốt hơn
            {
                // --- Seed Roles (Giữ nguyên code của bạn) ---
                if (!await _roleManager.RoleExistsAsync(WebsiteRoles.WebsiteAdmin))
                {
                    await _roleManager.CreateAsync(new IdentityRole(WebsiteRoles.WebsiteAdmin));
                }
                if (!await _roleManager.RoleExistsAsync(WebsiteRoles.WebsiteManager))
                {
                    await _roleManager.CreateAsync(new IdentityRole(WebsiteRoles.WebsiteManager));
                }
                if (!await _roleManager.RoleExistsAsync(WebsiteRoles.WebsiteUser))
                {
                    await _roleManager.CreateAsync(new IdentityRole(WebsiteRoles.WebsiteUser));
                }

                // --- Seed Admin User (Giữ nguyên code của bạn) ---
                if (!_context.Users.Any())
                {
                    var adminUser = new AppUser
                    {
                        UserName = "admin",
                        Email = "admin@gmail.com",
                        Name = "Super User",
                        EmailConfirmed = true // Important!
                    };

                    var createUserResult = await _userManager.CreateAsync(adminUser, "Admin@123"); // STRONG PASSWORD!

                    if (createUserResult.Succeeded)
                    {
                        var addToRoleResult = await _userManager.AddToRoleAsync(adminUser, WebsiteRoles.WebsiteAdmin);

                        if (!addToRoleResult.Succeeded)
                        {
                            // Ghi log lỗi thay vì throw exception để các seed khác có thể tiếp tục
                            Console.WriteLine($"Error: Failed to add admin to role. {string.Join(", ", addToRoleResult.Errors.Select(e => e.Description))}");
                            // throw new Exception("Failed to add admin to role." + string.Join(", ", addToRoleResult.Errors.Select(e => e.Description)));
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Error: Failed to create admin user. {string.Join(", ", createUserResult.Errors.Select(e => e.Description))}");
                        // throw new Exception("Failed to create admin user." + string.Join(", ", createUserResult.Errors.Select(e => e.Description)));
                    }
                }

                // --- >>> SEED SHIFTS <<< ---
                if (!_context.Shifts.Any()) // Chỉ seed nếu bảng Shifts chưa có dữ liệu
                {
                    var shifts = new List<Shift>
                    {
                        new Shift
                        {
                            Name = "Sáng",
                            StartTime = new TimeSpan(7, 0, 0), // 7:00 AM
                            EndTime = new TimeSpan(12, 0, 0), // 12:00 PM
                        },
                        new Shift
                        {
                            Name = "Chiều",
                            StartTime = new TimeSpan(12, 0, 0), // 12:00 PM
                            EndTime = new TimeSpan(18, 0, 0),  // 6:00 PM
                        },
                        new Shift
                        {
                            Name = "Tối",
                            StartTime = new TimeSpan(18, 0, 0), // 6:00 PM
                            EndTime = new TimeSpan(23, 0, 0), // 11:00 PM (Điều chỉnh nếu cần)
                        }
                        // Thêm các ca khác nếu cần
                    };

                    await _context.Shifts.AddRangeAsync(shifts);
                    await _context.SaveChangesAsync();
                    Console.WriteLine("Shifts seeded successfully."); // Thêm log
                }
                else
                {
                    Console.WriteLine("Shifts already exist. Skipping seeding."); // Thêm log
                }
                // --- END SEED SHIFTS ---

            }
            catch (Exception ex)
            {
                // Ghi log lỗi chi tiết
                Console.WriteLine($"Error during DB Initialization: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                // Có thể throw lại nếu muốn dừng ứng dụng khi có lỗi nghiêm trọng
                // throw;
            }
        }
    }
}
