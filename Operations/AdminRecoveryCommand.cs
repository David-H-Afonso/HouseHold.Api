using Household.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Household.Api.Operations;

public static class AdminRecoveryCommand
{
    public static async Task<int?> TryRunAsync(
        string[] args,
        IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        if (args.Length < 2 || !string.Equals(args[0], "admin", StringComparison.Ordinal))
            return null;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (string.Equals(args[1], "list", StringComparison.Ordinal))
        {
            var admins = await db
                .Users.AsNoTracking()
                .Where(user => user.IsAdmin)
                .OrderBy(user => user.Email)
                .Select(user => new { user.Email, user.UserName, user.IsActive })
                .ToListAsync(cancellationToken);

            if (admins.Count == 0)
            {
                Console.Error.WriteLine("No administrator accounts exist.");
                return 1;
            }

            foreach (var admin in admins)
                Console.WriteLine($"{admin.Email}\t{admin.UserName}\t{(admin.IsActive ? "active" : "inactive")}");
            return 0;
        }

        if (!string.Equals(args[1], "reset-password", StringComparison.Ordinal) || args.Length != 3)
        {
            WriteUsage();
            return 2;
        }

        var email = args[2].Trim();
        var adminUser = await db.Users.SingleOrDefaultAsync(
            user => user.IsAdmin && user.Email == email,
            cancellationToken
        );
        if (adminUser is null)
        {
            Console.Error.WriteLine("Administrator account not found.");
            return 1;
        }

        Console.Write("New password: ");
        var password = ReadSecret();
        Console.Write("Confirm password: ");
        var confirmation = ReadSecret();

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Passwords do not match.");
            return 2;
        }
        if (!PasswordMeetsRequirements(password))
        {
            Console.Error.WriteLine(
                "Password must contain at least 12 characters, including uppercase, lowercase, number, and symbol."
            );
            return 2;
        }

        adminUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        adminUser.IsActive = true;
        await db
            .RefreshTokens.Where(token => token.UserId == adminUser.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, DateTime.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        Console.WriteLine("Administrator password reset. Existing sessions were revoked.");
        return 0;
    }

    public static bool PasswordMeetsRequirements(string password) =>
        password.Length >= 12
        && password.Any(char.IsUpper)
        && password.Any(char.IsLower)
        && password.Any(char.IsDigit)
        && password.Any(character => !char.IsLetterOrDigit(character));

    private static string ReadSecret()
    {
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? string.Empty;

        var characters = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string(characters.ToArray());
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0)
                    characters.RemoveAt(characters.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                characters.Add(key.KeyChar);
        }
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet Household.Api.dll admin list");
        Console.Error.WriteLine("  dotnet Household.Api.dll admin reset-password <email>");
    }
}
