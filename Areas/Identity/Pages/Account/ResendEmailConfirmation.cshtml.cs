// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using POETWeb.Models;

namespace POETWeb.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ResendEmailConfirmationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IMemoryCache _cache;

        // Thời gian chờ giữa 2 lần gửi lại (per email)
        private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

        public ResendEmailConfirmationModel(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            IMemoryCache cache) 
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _cache = cache;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var normEmail = (Input.Email ?? string.Empty).Trim().ToLowerInvariant();
            var cacheKey = $"resend-email:{normEmail}";

            // Kiểm tra cooldown
            if (_cache.TryGetValue<DateTime>(cacheKey, out var lastSentUtc))
            {
                var elapsed = DateTime.UtcNow - lastSentUtc;
                if (elapsed < Cooldown)
                {
                    var remain = (int)Math.Ceiling((Cooldown - elapsed).TotalSeconds);
                    StatusMessage = $"Please wait {remain}s before requesting another confirmation email.";
                    ViewData["CooldownRemain"] = remain;
                    return Page();
                }
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);

            // 1) Email chưa đăng ký
            if (user == null)
            {
                StatusMessage = "This Email is not yet registered.";
                return Page();
            }

            // 2) Email đã xác nhận
            if (await _userManager.IsEmailConfirmedAsync(user))
            {
                StatusMessage = "This email is already confirmed.";
                return Page();
            }

            // 3) Email hợp lệ và chưa xác nhận → gửi lại mail xác nhận
            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId = userId, code = code },
                protocol: Request.Scheme);

            await _emailSender.SendEmailAsync(
                Input.Email,
                "Confirm your email",
                $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

            // Ghi nhận thời điểm gửi để kích hoạt cooldown
            _cache.Set(cacheKey, DateTime.UtcNow,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = Cooldown
                });

            StatusMessage = "Email xác nhận đã được gửi. Hãy kiểm tra hộp thư của bạn.";
            return Page();
        }
    }
}
