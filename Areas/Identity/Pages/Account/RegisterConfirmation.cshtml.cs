using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
using POETWeb.Models;

namespace POETWeb.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class RegisterConfirmationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IDistributedCache _cache;

        public RegisterConfirmationModel(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            IDistributedCache cache)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _cache = cache;
        }

        // Email được truyền qua query khi chuyển đến trang này sau đăng ký
        [BindProperty(SupportsGet = true)]
        public string? Email { get; set; }

        public void OnGet()
        {
            // chỉ để render; Email đã có từ query (?email=...)
        }

        public class ResendResponse
        {
            public bool ok { get; set; }
            public string message { get; set; } = "";
        }

        // AJAX: POST /Account/RegisterConfirmation?handler=Resend
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostResendAsync()
        {
            if (string.IsNullOrWhiteSpace(Email))
            {
                return new JsonResult(new ResendResponse { ok = false, message = "Missing email." })
                { StatusCode = 400 };
            }

            var user = await _userManager.FindByEmailAsync(Email);
            if (user == null)
            {
                return new JsonResult(new ResendResponse { ok = false, message = "This email is not registered." })
                { StatusCode = 404 };
            }

            if (await _userManager.IsEmailConfirmedAsync(user))
            {
                return new JsonResult(new ResendResponse { ok = false, message = "This email is already confirmed." })
                { StatusCode = 400 };
            }

            // Throttle 60s theo email
            var key = $"resend:confirm:{Email.ToLower()}";
            var hit = await _cache.GetStringAsync(key);
            if (hit != null)
            {
                return new JsonResult(new ResendResponse { ok = false, message = "Please wait 60 seconds before resending." })
                { StatusCode = 429 };
            }

            // Gửi lại mail xác nhận
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var enc = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page("/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId = user.Id, code = enc },
                protocol: Request.Scheme);

            await _emailSender.SendEmailAsync(
                Email,
                "Confirm your email",
                $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

            // đặt throttle 60s
            await _cache.SetStringAsync(
                key, "1",
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
                });

            return new JsonResult(new ResendResponse { ok = true, message = "Confirmation email sent." });
        }
    }
}
