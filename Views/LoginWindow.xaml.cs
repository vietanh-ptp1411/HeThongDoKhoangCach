using System.Windows;
using System.Windows.Input;

namespace HeThongDoKhoangCach.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(string currentCode)
    {
        InitializeComponent();
        CodeBox.Text = currentCode;
        Loaded += (_, _) => { CodeBox.Focus(); CodeBox.SelectAll(); };
    }

    /// <summary>Mã người kiểm tra đã nhập (null nếu hủy).</summary>
    public string? InspectorCode { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e) => Accept();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return) { Accept(); e.Handled = true; }
    }

    private void Accept()
    {
        var code = CodeBox.Text.Trim();
        if (code.Length == 0)
        {
            MessageBox.Show(this, "Vui lòng nhập mã người kiểm tra.", "Đăng nhập", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        InspectorCode = code;
        DialogResult = true;
    }
}
