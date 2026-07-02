using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DLVoiceLibrary.Core.Models;

public partial class Playlist : ObservableObject
{
    [ObservableProperty]
    private long _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private DateTime _updatedAt;

    public ObservableCollection<Track> Tracks { get; } = new();
}
