using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;

namespace POETWeb.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [StringLength(80, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        // 4 chữ cái + 4 số, ví dụ: ABCD1234
        [Required]
        [RegularExpression("^[A-Za-z]{4}\\d{4}$",
            ErrorMessage = "AccountCode phải gồm 4 chữ cái và 4 số (VD: ABCD1234).")]
        [StringLength(8)]
        public string AccountCode { get; set; } = string.Empty;

        [Url]
        [StringLength(2048)]
        public string? AvatarUrl { get; set; }
    }
}
