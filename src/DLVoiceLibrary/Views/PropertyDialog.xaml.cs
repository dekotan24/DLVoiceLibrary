using System.Windows;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;

namespace DLVoiceLibrary.Views;

/// <summary>
/// 作品メタデータの手動編集ダイアログ。作品IDがフォルダ名から取れなかった作品への後付けや、
/// タイトル/サークルの修正、ユーザータグの付与に使う。保存するまで元のVoiceWorkには触らない。
/// </summary>
public partial class PropertyDialog : Window
{
    private readonly VoiceWork _work;
    private readonly IDatabaseService _database;

    /// <summary>保存が行われたかどうか。呼び出し側が一覧の更新要否を判断するのに使う。</summary>
    public bool Saved { get; private set; }

    public PropertyDialog(VoiceWork work, IDatabaseService database)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _work = work;
        _database = database;

        TitleBox.Text = work.Title;
        CircleBox.Text = work.CircleName;
        ProductIdBox.Text = work.ProductId;
        VoiceActorsBox.Text = work.VoiceActors;
        UserTagsBox.Text = work.UserTags;
        MemoBox.Text = work.Memo;
        FolderPathText.Text = work.FolderPath;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var newProductId = ProductIdBox.Text.Trim().ToUpperInvariant();

        _work.Title = TitleBox.Text.Trim();
        _work.CircleName = CircleBox.Text.Trim();
        _work.VoiceActors = NormalizeCsv(VoiceActorsBox.Text);
        _work.UserTags = NormalizeCsv(UserTagsBox.Text);
        _work.Memo = MemoBox.Text;

        if (_work.ProductId != newProductId)
        {
            _work.ProductId = newProductId;
            // 作品IDが手動で設定されたらDLsite作品として扱い、再取得を可能にする
            if (!string.IsNullOrEmpty(newProductId))
            {
                _work.Source = "DLsite";
            }
        }

        try
        {
            await _database.UpdateWorkAsync(_work);
            Saved = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存に失敗しました:\n{ex.Message}", "DLVoiceLibrary",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>カンマ区切り入力の前後空白・空要素・重複を掃除する。</summary>
    private static string NormalizeCsv(string input)
    {
        var items = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(",", items);
    }
}
