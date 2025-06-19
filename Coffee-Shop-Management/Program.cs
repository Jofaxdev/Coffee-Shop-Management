using AspNetCoreHero.ToastNotification;
using AspNetCoreHero.ToastNotification.Extensions;
using Coffee_Shop_Management.Hubs;
using Coffee_Shop_Management.Models;
using Coffee_Shop_Management.Utilites;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;

namespace Coffee_Shop_Management
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            //Đăng ký loading lại trong mà không cần Ctrl + F5
            var builderRazor = builder.Services.AddRazorPages();
            //Đăng ký loading lại trong mà không cần Ctrl + F5 End


            //Xử lý session từ View
            builder.Services.AddHttpContextAccessor(); // Thêm dòng này
                                                       // Add services to the container.
            builder.Services.AddControllersWithViews().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                // Tùy chọn: Bỏ qua các thuộc tính null khi serialize JSON trả về client
                // options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                // Tùy chọn: Cho phép tên thuộc tính không phân biệt hoa thường khi deserialize (nếu cần)
                // options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            });

            builder.Services.AddSignalR(); // <--- Thêm dòng này

            //Xử lý session từ View
            builder.Services.AddHttpContextAccessor(); // AddHttpContextAccessor phải ở sau


            //Cấu hình Session
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30); // Thời gian tồn tại của session
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true; // Cho phép cookie hoạt động ngay cả khi không có sự đồng ý
            });


            builder.Services.AddDbContext<AppDbContext>(options =>
            {
                var connectString = builder.Configuration.GetConnectionString("CoffeeShopManagementDB");
                options.UseSqlServer(connectString);
            });


            // Dang ky Identity
            builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
            {
                // Cấu hình Lockout - khóa user
                //options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5); // Khóa 5 phút
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromSeconds(10); // Khóa trong 10 giây
                options.Lockout.MaxFailedAccessAttempts = 3; // Thất bại 3 lần thì khóa
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();


            builder.Services.AddScoped<IDbInitializer, DbInitializer>();

            //Đăng ký thông báo
            builder.Services.AddNotyf(config => { config.DurationInSeconds = 10; config.IsDismissable = true; config.Position = NotyfPosition.TopRight; });

            // Truy cập IdentityOptions
            builder.Services.Configure<IdentityOptions>(options =>
            {
                // Thiết lập về Password
                options.Password.RequireDigit = false; // Không bắt phải có số
                options.Password.RequireLowercase = false; // Không bắt phải có chữ thường
                options.Password.RequireNonAlphanumeric = false; // Không bắt ký tự đặc biệt
                options.Password.RequireUppercase = false; // Không bắt buộc chữ in
                options.Password.RequiredLength = 3; // Số ký tự tối thiểu của password
                options.Password.RequiredUniqueChars = 1; // Số ký tự riêng biệt

                // Cấu hình Lockout - khóa user
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromSeconds(10); // Khóa trong 10 giây
                options.Lockout.MaxFailedAccessAttempts = 3; // Thất bại 3 lần thì khóa
                options.Lockout.AllowedForNewUsers = true;

                // Cấu hình về User.
                options.User.AllowedUserNameCharacters = // các ký tự đặt tên user
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
                options.User.RequireUniqueEmail = true;  // Email là duy nhất

                // Cấu hình đăng nhập.
                //options.SignIn.RequireConfirmedEmail = true;            // Cấu hình xác thực địa chỉ email (email phải tồn tại)
                //options.SignIn.RequireConfirmedPhoneNumber = false;     // Xác thực số điện thoại
                //options.SignIn.RequireConfirmedAccount = true;

            });

            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/AccessDenied";
            });


            var app = builder.Build();

            DataSeedingAsync(); //Khởi tạo dữ liệu đầu tiên

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/Home/Error/{0}"); // Xử lý lỗi 404

            app.UseNotyf(); //Đăng ký thông báo

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseSession(); //Sử dụng session

            app.UseRouting();

            app.UseAuthorization();
            // Thêm MapHub cho SignalR ở đây
            app.MapHub<AppHub>("/appHub");

            app.MapControllerRoute(
                    name: "areas",
                    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();

            async Task DataSeedingAsync()
            {
                using (var scope = app.Services.CreateScope())
                {
                    var DbInitialize = scope.ServiceProvider.GetRequiredService<IDbInitializer>();
                    await DbInitialize.InitializeAsync();
                }
            }
        }
    }
}
