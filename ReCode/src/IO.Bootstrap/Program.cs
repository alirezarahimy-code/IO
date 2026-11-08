using IO.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

// Deliberately interactive and not exposed via HTTP. Migrations are NOT applied.
if (Console.IsInputRedirected || Console.IsOutputRedirected)
{
    Console.Error.WriteLine("A local interactive terminal is required; no redirected password input is accepted.");
    return 2;
}
var connection = Environment.GetEnvironmentVariable("ConnectionStrings__IO");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("An explicit ConnectionStrings__IO environment variable is required.");
    return 2;
}
Console.WriteLine("This creates the first administrator only if both account stores are empty.");
Console.WriteLine("Ensure both migrations have been applied to an explicitly authorized database.");
Console.Write("Type INITIALIZE to continue: ");
if (Console.ReadLine() != "INITIALIZE") return 2;
Console.Write("Administrator username: ");
var name = Console.ReadLine();
Console.Write("Password (not echoed): ");
var password = ReadSecret();
Console.Write("Repeat password (not echoed): ");
var repeated = ReadSecret();
if (string.IsNullOrWhiteSpace(name) || password != repeated)
{
    Console.Error.WriteLine("Input invalid or passwords do not match; no account created.");
    return 2;
}
try
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddIoIdentityStorage(connection);
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<InitialAdministrator>().CreateAsync(name, password);
    Console.WriteLine("Initial administrator created. No password was printed.");
    return 0;
}
catch (InvalidOperationException e) when (e.Message.StartsWith("Administrator creation rejected by identity validation:", StringComparison.Ordinal))
{
    Console.Error.WriteLine("Password or username did not satisfy the identity policy; no account created.");
    return 1;
}
catch (Exception)
{
    // Avoid printing connection details or sensitive exception content to the terminal.
    Console.Error.WriteLine("Initialization failed. Check database state and operational logs securely; no success was reported.");
    return 1;
}

static string ReadSecret()
{
    var value = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
        if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; continue; }
        if (!char.IsControl(key.KeyChar) && value.Length < 256) value.Append(key.KeyChar);
    }
}
