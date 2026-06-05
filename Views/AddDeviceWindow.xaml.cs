using System.Windows;
using System.Windows.Controls;
using Devices_MVVM.Devices.Models;
using Devices_MVVM.Models;

namespace Devices_MVVM.Views;

public partial class AddDeviceWindow : Window
{
    public AddDeviceWindow(IEnumerable<DeviceTemplateOption> templates, Func<string, string> idGenerator)
    {
        InitializeComponent();

        Templates = templates.ToList();
        _idGenerator = idGenerator;
        DeviceTypeComboBox.ItemsSource = Templates;
        DeviceTypeComboBox.SelectedIndex = 0;
    }

    private readonly Func<string, string> _idGenerator;

    public IReadOnlyList<DeviceTemplateOption> Templates { get; }
    public DeviceDefinition? DeviceDefinition { get; private set; }

    private void DeviceTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceTypeComboBox.SelectedItem is not DeviceTemplateOption template)
        {
            return;
        }

        DeviceIdTextBox.Text = _idGenerator(template.IdPrefix);
        DeviceNameTextBox.Text = template.DisplayName;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceTypeComboBox.SelectedItem is not DeviceTemplateOption template)
        {
            return;
        }

        string id = DeviceIdTextBox.Text.Trim();
        string name = DeviceNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "设备 Id 和设备名称不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DeviceDefinition = new DeviceDefinition
        {
            Id = id,
            Name = name,
            DeviceType = template.DeviceType,
            Kind = template.Kind,
            SkipCreate = true,
            Settings = new Dictionary<string, string>(template.Settings)
        };

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
