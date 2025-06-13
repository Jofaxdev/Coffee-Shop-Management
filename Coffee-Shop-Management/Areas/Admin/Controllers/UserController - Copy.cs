using AspNetCoreHero.ToastNotification.Abstractions;
using Coffee_Shop_Management.Areas.Admin.ViewModels;
using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages;

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AUserController : Controller
    {

        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly INotyfService _notification;
        public AUserController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, INotyfService notification)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _notification = notification;
        }


        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.ToListAsync();

            var vm = users.Select(x => new UserVM()
            {
                Id = x.Id,
                UserName = x.UserName,
                Email = x.Email,
            }).ToList();

            //assinging role
            foreach (var user in vm)
            {
                var singleUser = await _userManager.FindByIdAsync(user.Id);
                var role = await _userManager.GetRolesAsync(singleUser);
                user.Role = role.FirstOrDefault();
            }

            return View(vm);
        }

        [HttpGet("Logins")]
        public IActionResult Login()
        {
            if (!HttpContext.User.Identity!.IsAuthenticated)
            {
                return View(new LoginVM());
            }
            return RedirectToAction("Index", "Product", new { area = "Admin" });
        }

        [HttpPost("Logins")]
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
