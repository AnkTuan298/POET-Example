// Licensed under MIT
#nullable disable
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using POETWeb.Models;

namespace POETWeb.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IWebHostEnvironment _env;

        private static readonly string[] AllowedContentTypes = new[]
        {
            "image/jpeg", "image/png", "image/webp"
        };
        private const long MaxFileBytes = 5L * 1024 * 1024; // 5 MB
        private const string UploadFolder = "uploads/avatars"; // under wwwroot

        public IndexModel(UserManager<ApplicationUser> userManager,
                          SignInManager<ApplicationUser> signInManager,
                          IWebHostEnvironment env)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _env = env;
        }

        [TempData]
        public string StatusMessage { get; set; }

        public string Username { get; set; }
        public string AccountCode { get; set; }

        [BindProperty]
        public InputModel Input { get; set; }

        // file upload
        [BindProperty]
        public IFormFile AvatarFile { get; set; }

        public class InputModel
        {
            [Required]
            [Display(Name = "Full Name")]
            [StringLength(80, MinimumLength = 2)]
            public string FullName { get; set; }

            [Phone]
            [RegularExpression(@"^(0|\+84)(\d{9})$",
            ErrorMessage = "Invalid Phone Number. Please enter 10 digits number start by 0 or +84")]
            [Display(Name = "Phone Number")]
            public string PhoneNumber { get; set; }

            [Display(Name = "Avatar URL")]
            [Url(ErrorMessage = "Please enter a valid URL")]
            public string AvatarUrl { get; set; } // vẫn giữ để ai muốn dán URL ngoài cũng được
        }

        private async Task LoadAsync(ApplicationUser user)
        {
            Username = await _userManager.GetUserNameAsync(user);
            AccountCode = user.AccountCode;
            Input = new InputModel
            {
                FullName = user.FullName,
                PhoneNumber = await _userManager.GetPhoneNumberAsync(user),
                AvatarUrl = user.AvatarUrl
            };
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("User not found.");
            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("User not found.");

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            // Update FullName & AvatarUrl (nếu người dùng tự dán URL)
            var hasChanges = false;
            if (user.FullName != Input.FullName?.Trim())
            {
                user.FullName = Input.FullName?.Trim();
                hasChanges = true;
            }

            // Update phone qua UserManager
            var currentPhone = await _userManager.GetPhoneNumberAsync(user);
            if (Input.PhoneNumber != currentPhone)
            {
                var setPhoneResult = await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
                if (!setPhoneResult.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, "Failed to update phone number.");
                    await LoadAsync(user);
                    return Page();
                }
            }

            // Nếu có upload file ảnh
            if (AvatarFile != null && AvatarFile.Length > 0)
            {
                // 1) kiểm tra size
                if (AvatarFile.Length > MaxFileBytes)
                {
                    ModelState.AddModelError(string.Empty, "Avatar must be 5MB or smaller.");
                    await LoadAsync(user);
                    return Page();
                }

                // 2) kiểm tra content-type
                if (!AllowedContentTypes.Contains(AvatarFile.ContentType))
                {
                    ModelState.AddModelError(string.Empty, "Only JPG, PNG or WebP are allowed.");
                    await LoadAsync(user);
                    return Page();
                }

                // 3) chuẩn bị thư mục
                var wwwroot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var folderPath = Path.Combine(wwwroot, UploadFolder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(folderPath);

                // 4) tạo tên file an toàn
                var ext = Path.GetExtension(AvatarFile.FileName);
                // map ext theo content-type nếu thiếu
                if (string.IsNullOrEmpty(ext))
                {
                    ext = AvatarFile.ContentType switch
                    {
                        "image/jpeg" => ".jpg",
                        "image/png" => ".png",
                        "image/webp" => ".webp",
                        _ => ".bin"
                    };
                }
                var safeFileName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
                var savePath = Path.Combine(folderPath, safeFileName);

                // 5) lưu file
                using (var stream = System.IO.File.Create(savePath))
                {
                    await AvatarFile.CopyToAsync(stream);
                }

                // 6) xoá ảnh cũ nếu nó nằm trong thư mục uploads của mình
                if (!string.IsNullOrWhiteSpace(user.AvatarUrl)
                    && user.AvatarUrl.StartsWith($"/{UploadFolder}", StringComparison.OrdinalIgnoreCase))
                {
                    var oldPhysical = Path.Combine(wwwroot, user.AvatarUrl.TrimStart('/')
                                                              .Replace('/', Path.DirectorySeparatorChar));
                    try { if (System.IO.File.Exists(oldPhysical)) System.IO.File.Delete(oldPhysical); }
                    catch { /* nuốt lỗi xoá file, không làm hỏng flow */ }
                }

                // 7) set URL tương đối
                var relativeUrl = "/" + UploadFolder + "/" + safeFileName;
                Input.AvatarUrl = relativeUrl; // đồng bộ với field hiển thị
                user.AvatarUrl = relativeUrl;
                hasChanges = true;
            }
            else
            {
                // không upload file, nhưng nếu user gõ URL khác thì set
                var normalized = string.IsNullOrWhiteSpace(Input.AvatarUrl) ? null : Input.AvatarUrl.Trim();
                if (user.AvatarUrl != normalized)
                {
                    user.AvatarUrl = normalized;
                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                var update = await _userManager.UpdateAsync(user);
                if (!update.Succeeded)
                {
                    ModelState.AddModelError(string.Empty, "Failed to update profile.");
                    await LoadAsync(user);
                    return Page();
                }
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Your profile has been updated.";
            return RedirectToPage();
        }
    }
}
