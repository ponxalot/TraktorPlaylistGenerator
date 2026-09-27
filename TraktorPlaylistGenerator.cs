using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

public class TraktorPlaylistGenerator
{
    public string MusicRoot { get; }
    public string CollectionPath { get; }
    public string PlaylistFolderName { get; }

    private readonly HashSet<string> _audioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".aiff", ".aif", ".flac", ".m4a", ".ogg"
    };

    public TraktorPlaylistGenerator(string musicRoot, string collectionPath, string playlistFolderName = "Folder Playlists")
    {
        MusicRoot = musicRoot;
        CollectionPath = collectionPath;
        PlaylistFolderName = playlistFolderName;
    }

    public void Generate()
    {
        EnsureCollectionExists();

        BackupCollection();

        var doc = XDocument.Load(CollectionPath, LoadOptions.PreserveWhitespace);

        var playlistsNode = FindPlaylistsNode(doc);
        RemoveOldFolder(playlistsNode);

        var newFolderNode = CreateFolderNode();
        var targetSubnodes = newFolderNode.Element("SUBNODES")
                             ?? throw new Exception("SUBNODES im neuen Playlist-Ordner nicht gefunden.");

        var genreFolders = GetGenreFolders();

        foreach (var genreFolder in genreFolders)
        {
            var playlistNode = CreatePlaylistNode(genreFolder);
            targetSubnodes.Add(playlistNode);
        }

        targetSubnodes.SetAttributeValue("COUNT", targetSubnodes.Elements("NODE").Count());

        playlistsNode.Add(newFolderNode);
        playlistsNode.SetAttributeValue("COUNT", playlistsNode.Elements("NODE").Count());

        doc.Save(CollectionPath);

        Console.WriteLine("Playlists erfolgreich generiert.");
    }

    private void EnsureCollectionExists()
    {
        if (!File.Exists(CollectionPath))
            throw new FileNotFoundException("collection.nml nicht gefunden", CollectionPath);
    }

    private void BackupCollection()
    {
        var backupPath = $"{CollectionPath}.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
        File.Copy(CollectionPath, backupPath);
        Console.WriteLine($"Backup erstellt: {backupPath}");
    }

    private XElement FindPlaylistsNode(XDocument doc)
    {
        var playlists = doc.Root?.Element("PLAYLISTS");

        if (playlists == null)
            throw new Exception("PLAYLISTS-Element nicht gefunden.");

        var rootNode = playlists.Elements("NODE")
            .FirstOrDefault(x =>
                (string?)x.Attribute("TYPE") == "FOLDER" &&
                (string?)x.Attribute("NAME") == "$ROOT");

        if (rootNode == null)
            throw new Exception("PLAYLISTS $ROOT-Node nicht gefunden.");

        var subnodes = rootNode.Element("SUBNODES");

        if (subnodes == null)
            throw new Exception("SUBNODES unter PLAYLISTS/$ROOT nicht gefunden.");

        return subnodes;
    }

    private void RemoveOldFolder(XElement playlistsNode)
    {
        var old = playlistsNode.Elements("NODE")
            .FirstOrDefault(x =>
                (string?)x.Attribute("TYPE") == "FOLDER" &&
                (string?)x.Attribute("NAME") == PlaylistFolderName);

        old?.Remove();
    }

    private XElement CreateFolderNode()
    {
        return new XElement("NODE",
            new XAttribute("TYPE", "FOLDER"),
            new XAttribute("NAME", PlaylistFolderName),
            new XElement("SUBNODES",
                new XAttribute("COUNT", "0")
            )
        );
    }

    private List<string> GetGenreFolders()
    {
        return Directory.GetDirectories(MusicRoot)
            .Where(dir => Path.GetFileName(dir) != "_TraktorData")
            .OrderBy(dir => Path.GetFileName(dir), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private XElement CreatePlaylistNode(string genreFolder)
    {
        var playlistName = Path.GetFileName(genreFolder);

        var files = Directory.GetFiles(genreFolder)
            .Where(f => _audioExtensions.Contains(Path.GetExtension(f)))
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();

        var playlist = new XElement("PLAYLIST",
            new XAttribute("ENTRIES", files.Count),
            new XAttribute("TYPE", "LIST"),
            new XAttribute("UUID", Guid.NewGuid().ToString("N"))
        );

        foreach (var file in files)
        {
            var key = ToTraktorKey(file);

            playlist.Add(
                new XElement("ENTRY",
                    new XElement("PRIMARYKEY",
                        new XAttribute("TYPE", "TRACK"),
                        new XAttribute("KEY", key)
                    )
                )
            );
        }

        Console.WriteLine($"{playlistName}: {files.Count} Tracks");

        return new XElement("NODE",
            new XAttribute("TYPE", "PLAYLIST"),
            new XAttribute("NAME", playlistName),
            playlist
        );
    }

    private string ToTraktorKey(string path)
    {
        var full = Path.GetFullPath(path);
        var drive = Path.GetPathRoot(full)!.TrimEnd('\\');
        var rest = full.Substring(Path.GetPathRoot(full)!.Length);

        var parts = rest.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => !string.IsNullOrWhiteSpace(p));

        return drive + "/:" + string.Join("/:", parts);
    }
}