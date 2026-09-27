namespace TraktorPlaylistGenerator;

public sealed class TraktorPlaylistOptions
{
    public required string MusicRoot { get; init; }

    public string TraktorDataFolder { get; init; }
        = "_TraktorData";

    public string PlaylistFolderName { get; init; }
        = "Folder Playlists";

    /// <summary>
    /// Set to true for a dry run without changing the collection.nml
    /// </summary>
    public bool DryRun { get; init; } = false;

    public HashSet<string> IgnoredFolders { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> AudioExtensions { get; init; }
        = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3",
            ".wav",
            ".aiff",
            ".aif",
            ".flac",
            ".m4a",
            ".ogg"
        };

    public string CollectionPath =>
        Path.Combine(
            MusicRoot,
            TraktorDataFolder,
            "collection.nml");
}