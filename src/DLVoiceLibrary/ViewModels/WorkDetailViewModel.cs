using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DLVoiceLibrary.Core.Models;
using DLVoiceLibrary.Core.Services;

namespace DLVoiceLibrary.ViewModels;

/// <summary>選択中の作品のトラック一覧を、フォルダ階層をそのまま反映したツリーとして保持する。</summary>
public partial class WorkDetailViewModel : ObservableObject
{
    private readonly IDatabaseService _database;

    public WorkDetailViewModel(IDatabaseService database)
    {
        _database = database;
    }

    [ObservableProperty]
    private VoiceWork? _work;

    [ObservableProperty]
    private ObservableCollection<TrackTreeNode> _trackTree = new();

    [ObservableProperty]
    private bool _isLoading;

    public async Task LoadAsync(VoiceWork work, CancellationToken ct = default)
    {
        Work = work;
        IsLoading = true;
        try
        {
            var tracks = await _database.GetTracksByWorkIdAsync(work.Id, ct);

            work.Tracks.Clear();
            foreach (var track in tracks)
            {
                work.Tracks.Add(track);
            }

            TrackTree = TrackTreeBuilder.Build(work.FolderPath, tracks);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Clear()
    {
        Work = null;
        TrackTree = new ObservableCollection<TrackTreeNode>();
    }
}
