namespace Devices_MVVM.Models;

public sealed class LoginConfig
{
    public List<LoginAccount> Accounts { get; set; } = new();
}

public sealed class LoginAccount
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
