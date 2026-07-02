namespace DLVoiceLibrary.Core.Models;

public sealed record PlaylistItem(long Id, long PlaylistId, long TrackId, int Position);
