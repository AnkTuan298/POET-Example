// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using POETWeb.Models;

namespace POETWeb.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;  // thêm UserManager để tìm user
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(SignInManager<ApplicationUser> signInManager,
                          UserManager<ApplicationUser> userManager,
                          ILogger<LoginModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }
        public IList<AuthenticationScheme> ExternalLogins { get; set; }
        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [Display(Name = "Email or Username")]
            public string Login { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            // Validate cơ bản phía server
            if (string.IsNullOrWhiteSpace(Input.Login))
            {
                ModelState.AddModelError(string.Empty, "Please enter your username or email.");
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Input.Password))
            {
                ModelState.AddModelError(string.Empty, "Please enter your password.");
                return Page();
            }

            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Tìm user theo email hoặc username
            var user = await _userManager.FindByEmailAsync(Input.Login)
                    ?? await _userManager.FindByNameAsync(Input.Login);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt. Please check your credentials.");
                return Page();
            }

            // Kiểm tra lockout
            if (await _userManager.IsLockedOutAsync(user))
            {
                ModelState.AddModelError(string.Empty, "Your account is locked. Please try again later.");
                return Page();
            }

            // Kiểm tra email đã xác nhận
            if (!await _userManager.IsEmailConfirmedAsync(user))
            {
                ModelState.AddModelError(string.Empty,
                    "Please confirm your email before logging in. Check your inbox for the confirmation link.");
                return Page();
            }

            // Đăng nhập bằng username
            var result = await _signInManager.PasswordSignInAsync(
                user.UserName, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                var redirectUrl = await GetRoleRedirectUrlAsync(user);
                return LocalRedirect(redirectUrl);
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                ModelState.AddModelError(string.Empty, "Too many failed attempts. Your account has been temporarily locked.");
                return Page();
            }

            // Sai mật khẩu hoặc thông tin không đúng
            ModelState.AddModelError(string.Empty, "Invalid username/email or password.");
            return Page();
        }


        private async Task<string> GetRoleRedirectUrlAsync(ApplicationUser user)
        {
            if (user == null) return Url.Content("~/");

            if (await _userManager.IsInRoleAsync(user, "Admin"))
                return Url.Content("~/Admin");

            if (await _userManager.IsInRoleAsync(user, "Teacher"))
                return Url.Content("~/Teacher");

            // Mặc định Student
            return Url.Content("~/Student");
        }
    }
}
