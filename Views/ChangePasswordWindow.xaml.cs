using System.Windows;

namespace Devices_MVVM.Views;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow()
    {
        InitializeComponent();
    }

    public string NewPassword => NewPasswordBox.Password;

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewPasswordBox.Password))
        {
            MessageBox.Show(this, "新密码不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            MessageBox.Show(this, "两次输入的新密码不一致", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
