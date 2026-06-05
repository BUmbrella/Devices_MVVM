using System.IO;
using System.Text.Json;
using System.Windows;
using Devices_MVVM.Models;

namespace Devices_MVVM.Views;

public partial class LoginWindow : Window
{
    private readonly List<LoginAccount> _accounts;

    public LoginWindow()
    {
        InitializeComponent();
        _accounts = LoadAccounts();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_accounts.Any(account =>
                string.Equals(account.UserName, UserNameTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase) &&
                account.Password == PasswordBox.Password))
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, "账号或密码错误", "登录失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static List<LoginAccount> LoadAccounts()
    {
        string configPath = GetLoginConfigPath();
        EnsureLoginConfig(configPath);

        string json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<LoginConfig>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return config?.Accounts ?? new List<LoginAccount>();
    }

    private static void EnsureLoginConfig(string configPath)
    {
        string? dataFolder = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(dataFolder) && !Directory.Exists(dataFolder))
        {
            Directory.CreateDirectory(dataFolder);
        }

        if (File.Exists(configPath))
        {
            return;
        }

        var config = new LoginConfig
        {
            Accounts =
            [
                new LoginAccount
                {
                    UserName = "admin",
                    Password = "123456"
                }
            ]
        };

        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GetLoginConfigPath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "login_config.json");
    }
}
