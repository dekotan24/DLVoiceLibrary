using System.Windows;

namespace DLVoiceLibrary.Views;

/// <summary>プレイリスト名の作成・リネーム等、単純な1行テキスト入力に使う汎用ダイアログ。</summary>
public partial class TextInputDialog : Window
{
    public string InputText { get; private set; } = string.Empty;

    public TextInputDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialValue;
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        InputText = InputBox.Text.Trim();
        DialogResult = !string.IsNullOrWhiteSpace(InputText);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
