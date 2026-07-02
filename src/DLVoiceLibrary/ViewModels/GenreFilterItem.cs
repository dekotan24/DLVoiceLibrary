using CommunityToolkit.Mvvm.ComponentModel;

namespace DLVoiceLibrary.ViewModels;

/// <summary>詳細検索パネルのジャンルタグチップ1個分。選択状態が変わったらフィルタ再適用を通知する。</summary>
public sealed partial class GenreFilterItem : ObservableObject
{
    private readonly Action _onSelectionChanged;

    public GenreFilterItem(string name, int count, Action onSelectionChanged)
    {
        Name = name;
        Count = count;
        _onSelectionChanged = onSelectionChanged;
    }

    public string Name { get; }

    /// <summary>このタグを持つ作品数(表示用)。</summary>
    public int Count { get; }

    public string Display => $"{Name} ({Count})";

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();
}
