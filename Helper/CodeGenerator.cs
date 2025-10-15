using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POETWeb.Models;

namespace POETWeb.Helpers
{
    public static partial class CodeGenerator
    {
        // Bỏ I và O
        private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";

        public static string GenerateAccountCode() => GenerateAccountCodeSecure();

        public static string GenerateAccountCodeSecure()
        {
            Span<char> buf = stackalloc char[8];

            // 4 chữ
            for (int i = 0; i < 4; i++)
                buf[i] = Letters[RandomNumberGenerator.GetInt32(Letters.Length)];

            // 4 số zero-pad
            var digits = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
            buf[4] = digits[0];
            buf[5] = digits[1];
            buf[6] = digits[2];
            buf[7] = digits[3];

            return new string(buf);
        }

        // Sinh mã không trùng trong DB
        public static async Task<string> GenerateUniqueAccountCodeAsync(
            UserManager<ApplicationUser> userManager, int maxTry = 6)
        {
            for (int i = 0; i < maxTry; i++)
            {
                var code = GenerateAccountCodeSecure();
                var exists = await userManager.Users.AnyAsync(u => u.AccountCode == code);
                if (!exists) return code;
            }
            throw new InvalidOperationException("Can not generate unique AccountCode.");
        }
        // Sinh mã lớp 6 ký tự
        public static string GenerateClassCode6()
        {
            const string bag = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789"; // bỏ I,O
            Span<char> buf = stackalloc char[6];
            for (int i = 0; i < 6; i++)
                buf[i] = bag[RandomNumberGenerator.GetInt32(bag.Length)];
            return new string(buf);
        }


    }
}
