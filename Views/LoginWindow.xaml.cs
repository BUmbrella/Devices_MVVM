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

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        string userName = UserNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userName))
        {
            MessageBox.Show(this, "请先输入账号", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoginAccount? account = _accounts.FirstOrDefault(item =>
            string.Equals(item.UserName, userName, StringComparison.OrdinalIgnoreCase));

        if (account == null || account.Password != PasswordBox.Password)
        {
            MessageBox.Show(this, "当前账号或密码不正确，不能修改密码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new ChangePasswordWindow
        {
            Owner = this
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        account.Password = window.NewPassword;
        SaveAccounts(_accounts);
        PasswordBox.Password = window.NewPassword;

        MessageBox.Show(this, "密码修改成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private static void SaveAccounts(List<LoginAccount> accounts)
    {
        string configPath = GetLoginConfigPath();
        string? dataFolder = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(dataFolder) && !Directory.Exists(dataFolder))
        {
            Directory.CreateDirectory(dataFolder);
        }

        var config = new LoginConfig
        {
            Accounts = accounts
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
