using System.Diagnostics;
using System.Xml.Linq;

namespace TraktorPlaylistGenerator;

public sealed class TraktorPlaylistGenerator
{
    private sealed record TrackInfo(
        string TraktorKey);
    
    private sealed record CollectionTrack(
        string WindowsPath,
        string TraktorKey);
    
    
    private readonly TraktorPlaylistOptions _options;

    private readonly Dictionary<string, TrackInfo> _tracks = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<CollectionTrack> _collectionTracks = [];

    private Dictionary<string, string> _existingPlaylistUuids = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _missingTracks = [];

    public TraktorPlaylistGenerator(
        TraktorPlaylistOptions options)
    {
        _options = options;
    }

    public void Generate()
    {
        ValidatePaths();

        // collection.nml niemals verändern,
        // während Traktor läuft.
        if (!_options.DryRun)
        {
            EnsureTraktorIsNotRunning();
        }

        Console.WriteLine("======================================");
        Console.WriteLine(" Traktor Playlist Generator");
        Console.WriteLine("======================================");
        Console.WriteLine();

        Console.WriteLine($"Musik:      {_options.MusicRoot}");
        Console.WriteLine($"Collection: {_options.CollectionPath}");
        Console.WriteLine($"Dry Run:    {_options.DryRun}");
        Console.WriteLine();

        var doc = XDocument.Load(
            _options.CollectionPath,
            LoadOptions.PreserveWhitespace);

        ValidateCollection(doc);

        var nmlVersion =
            (string?)doc.Root?.Attribute("VERSION")
            ?? "unbekannt";

        Console.WriteLine($"NML-Version: {nmlVersion}");

        // Baut sowohl _tracks als auch _collectionTracks auf.
        BuildTrackIndex(doc);

        Console.WriteLine(
            $"Tracks in Collection: {_tracks.Count:N0}");

        var playlistRoot =
            FindPlaylistRoot(doc);

        ReadExistingPlaylistUuids(playlistRoot);

        Console.WriteLine(
            $"Vorhandene Playlist-UUIDs: " +
            $"{_existingPlaylistUuids.Count}");

        var existingFolder =
            FindGeneratedFolder(playlistRoot);

        existingFolder?.Remove();

        var generatedFolder =
            CreateFolderNode();

        var subnodes =
            generatedFolder.Element("SUBNODES")
            ?? throw new InvalidOperationException(
                "SUBNODES konnte nicht erstellt werden.");

        var folders =
            GetMusicFolders();

        Console.WriteLine();
        Console.WriteLine(
            $"{folders.Count} Musikordner gefunden.");
        Console.WriteLine();

        foreach (var folder in folders)
        {
            var playlist =
                CreatePlaylistNode(folder);

            subnodes.Add(playlist);
        }

        subnodes.SetAttributeValue(
            "COUNT",
            subnodes.Elements("NODE").Count());

        playlistRoot.Add(generatedFolder);

        playlistRoot.SetAttributeValue(
            "COUNT",
            playlistRoot.Elements("NODE").Count());

        // Bisherige Zusammenfassung
        PrintSummary(folders.Count);

        // NEU:
        // Bei fehlenden Tracks nach alten/verwaisten
        // Collection-Einträgen im selben Ordner suchen.
        if (_missingTracks.Count > 0)
        {
            PrintMissingTrackDiagnostics();
        }

        if (_options.DryRun)
        {
            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Yellow;

            Console.WriteLine(
                "DRY RUN: collection.nml wurde NICHT verändert.");

            Console.ResetColor();

            return;
        }

        WriteCollectionAtomically(doc);
    }

    // ---------------------------------------------------------
    // Validation
    // ---------------------------------------------------------
    
    private static void EnsureTraktorIsNotRunning()
    {
        // Nur echte Traktor-Anwendungen berücksichtigen.
        // Unser eigener Prozess "TraktorPlaylistGenerator"
        // wird dadurch nicht mehr erfasst.

        var currentProcessId =
            Environment.ProcessId;

        var traktorProcesses = Process
            .GetProcesses()
            .Where(p =>
            {
                try
                {
                    // Eigenen Prozess grundsätzlich ignorieren
                    if (p.Id == currentProcessId)
                        return false;

                    var name = p.ProcessName;

                    // Typische Traktor-Prozessnamen:
                    //
                    // Traktor
                    // Traktor Pro 3
                    // Traktor Pro 4
                    //
                    // Aber NICHT:
                    // TraktorPlaylistGenerator

                    return
                        string.Equals(
                            name,
                            "Traktor",
                            StringComparison.OrdinalIgnoreCase)

                        ||

                        name.StartsWith(
                            "Traktor Pro",
                            StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            })
            .ToList();

        if (traktorProcesses.Count == 0)
            return;

        var processNames = string.Join(
            ", ",
            traktorProcesses
                .Select(p => p.ProcessName)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase));

        throw new InvalidOperationException(
            "Traktor läuft noch.\n\n" +
            $"Gefundener Prozess: {processNames}\n\n" +
            "Bitte Traktor vollständig beenden und den " +
            "Playlist-Generator danach erneut starten.");
    }

    private void ValidatePaths()
    {
        if (!Directory.Exists(_options.MusicRoot))
        {
            throw new DirectoryNotFoundException(
                $"Musikordner nicht gefunden:\n" +
                _options.MusicRoot);
        }

        if (!File.Exists(_options.CollectionPath))
        {
            throw new FileNotFoundException(
                "collection.nml nicht gefunden.",
                _options.CollectionPath);
        }
    }

    private static void ValidateCollection(
        XDocument doc)
    {
        if (doc.Root == null)
        {
            throw new InvalidOperationException(
                "Ungültige XML-Datei.");
        }

        if (!string.Equals(
                doc.Root.Name.LocalName,
                "NML",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Die Datei scheint keine Traktor-NML-Datei zu sein.");
        }

        if (doc.Root.Element("COLLECTION") == null)
        {
            throw new InvalidOperationException(
                "COLLECTION fehlt in der NML-Datei.");
        }

        if (doc.Root.Element("PLAYLISTS") == null)
        {
            throw new InvalidOperationException(
                "PLAYLISTS fehlt in der NML-Datei.");
        }
    }

    // ---------------------------------------------------------
    // Track collection
    // ---------------------------------------------------------

    private void BuildTrackIndex(XDocument doc)
    {
        _tracks.Clear();
        _collectionTracks.Clear();

        var collection =
            doc.Root?.Element("COLLECTION")
            ?? throw new InvalidOperationException(
                "COLLECTION wurde in collection.nml nicht gefunden.");

        foreach (var entry in collection.Elements("ENTRY"))
        {
            var location =
                entry.Element("LOCATION");

            if (location == null)
                continue;

            var volume =
                (string?)location.Attribute("VOLUME");

            var dir =
                (string?)location.Attribute("DIR");

            var file =
                (string?)location.Attribute("FILE");

            if (string.IsNullOrWhiteSpace(volume) ||
                string.IsNullOrWhiteSpace(file))
            {
                continue;
            }

            // DIR kann theoretisch leer sein.
            dir ??= string.Empty;

            var windowsPath =
                TraktorToWindowsPath(
                    volume,
                    dir,
                    file);

            if (string.IsNullOrWhiteSpace(windowsPath))
                continue;

            var traktorKey =
                volume + dir + file;

            var normalizedPath =
                NormalizePath(windowsPath);

            // Für das eigentliche Playlist-Matching:
            //
            // Windows-Pfad -> Traktor PRIMARYKEY
            _tracks[normalizedPath] =
                new TrackInfo(
                    traktorKey);

            // Zusätzlich für unsere Diagnose von
            // umbenannten / ersetzten Dateien.
            _collectionTracks.Add(
                new CollectionTrack(
                    windowsPath,
                    traktorKey));
        }

        Console.WriteLine(
            $"Tracks in Traktor Collection: " +
            $"{_tracks.Count:N0}");
    }

    private static string? TraktorToWindowsPath(
        string volume,
        string dir,
        string file)
    {
        // Dieser Generator ist für eine Windows-
        // Traktor-Collection vorgesehen.

        if (!volume.EndsWith(':'))
            return null;

        var directory =
            dir.Replace("/:", "\\");

        directory =
            directory.TrimStart('\\');

        try
        {
            return Path.GetFullPath(
                Path.Combine(
                    volume + "\\",
                    directory,
                    file));
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizePath(
        string path)
    {
        return Path
            .GetFullPath(path)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
    }

    // ---------------------------------------------------------
    // Playlist tree
    // ---------------------------------------------------------

    private static XElement FindPlaylistRoot(
        XDocument doc)
    {
        var playlists =
            doc.Root!.Element("PLAYLISTS")
            ?? throw new InvalidOperationException(
                "PLAYLISTS nicht gefunden.");

        var rootNode =
            playlists
                .Elements("NODE")
                .FirstOrDefault(x =>
                    string.Equals(
                        (string?)x.Attribute("TYPE"),
                        "FOLDER",
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    string.Equals(
                        (string?)x.Attribute("NAME"),
                        "$ROOT",
                        StringComparison.OrdinalIgnoreCase));

        if (rootNode == null)
        {
            throw new InvalidOperationException(
                "PLAYLISTS/$ROOT nicht gefunden.");
        }

        return rootNode.Element("SUBNODES")
               ?? throw new InvalidOperationException(
                   "SUBNODES unter $ROOT nicht gefunden.");
    }

    private XElement? FindGeneratedFolder(
        XElement playlistRoot)
    {
        return playlistRoot
            .Elements("NODE")
            .FirstOrDefault(x =>
                string.Equals(
                    (string?)x.Attribute("TYPE"),
                    "FOLDER",
                    StringComparison.OrdinalIgnoreCase)
                &&
                string.Equals(
                    (string?)x.Attribute("NAME"),
                    _options.PlaylistFolderName,
                    StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------
    // Preserve UUIDs
    // ---------------------------------------------------------

    private void ReadExistingPlaylistUuids(
        XElement playlistRoot)
    {
        var folder =
            FindGeneratedFolder(playlistRoot);

        if (folder == null)
            return;

        var subnodes =
            folder.Element("SUBNODES");

        if (subnodes == null)
            return;

        foreach (var node in
                 subnodes.Elements("NODE"))
        {
            if (!string.Equals(
                    (string?)node.Attribute("TYPE"),
                    "PLAYLIST",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name =
                (string?)node.Attribute("NAME");

            var playlist =
                node.Element("PLAYLIST");

            var uuid =
                (string?)playlist?.Attribute("UUID");

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(uuid))
            {
                continue;
            }

            _existingPlaylistUuids[name] = uuid;
        }
    }

    private string GetPlaylistUuid(
        string playlistName)
    {
        if (_existingPlaylistUuids.TryGetValue(
                playlistName,
                out var existingUuid))
        {
            return existingUuid;
        }

        return Guid
            .NewGuid()
            .ToString("N")
            .ToUpperInvariant();
    }

    // ---------------------------------------------------------
    // Folder scanning
    // ---------------------------------------------------------

    private List<string> GetMusicFolders()
    {
        return Directory
            .EnumerateDirectories(
                _options.MusicRoot,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(folder =>
                !ShouldIgnoreFolder(folder))
            .OrderBy(
                Path.GetFileName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private bool ShouldIgnoreFolder(string folder)
    {
        var name = Path.GetFileName(folder);

        // Traktor-Datenverzeichnis niemals als Playlist behandeln
        if (string.Equals(
                name,
                _options.TraktorDataFolder,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_options.IgnoredFolders.Contains(name))
            return true;

        // versteckte/Punkt-Ordner
        if (name.StartsWith('.'))
            return true;

        return false;
    }

    // ---------------------------------------------------------
    // Generate playlists
    // ---------------------------------------------------------

    private XElement CreateFolderNode()
    {
        return new XElement(
            "NODE",

            new XAttribute(
                "TYPE",
                "FOLDER"),

            new XAttribute(
                "NAME",
                _options.PlaylistFolderName),

            new XElement(
                "SUBNODES",
                new XAttribute("COUNT", 0)));
    }

    private XElement CreatePlaylistNode(
        string folder)
    {
        var playlistName =
            Path.GetFileName(folder);

        var files =
            Directory
                .EnumerateFiles(
                    folder,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Where(file =>
                    _options.AudioExtensions.Contains(
                        Path.GetExtension(file)))
                .OrderByDescending(
                    File.GetLastWriteTime)
                .ToList();

        var tracks =
            new List<TrackInfo>();

        foreach (var file in files)
        {
            var normalized =
                NormalizePath(file);

            if (_tracks.TryGetValue(
                    normalized,
                    out var track))
            {
                tracks.Add(track);
            }
            else
            {
                _missingTracks.Add(file);
            }
        }

        var uuid =
            GetPlaylistUuid(playlistName);

        var playlist =
            new XElement(
                "PLAYLIST",

                new XAttribute(
                    "ENTRIES",
                    tracks.Count),

                new XAttribute(
                    "TYPE",
                    "LIST"),

                new XAttribute(
                    "UUID",
                    uuid));

        foreach (var track in tracks)
        {
            playlist.Add(
                new XElement(
                    "ENTRY",

                    new XElement(
                        "PRIMARYKEY",

                        new XAttribute(
                            "TYPE",
                            "TRACK"),

                        new XAttribute(
                            "KEY",
                            track.TraktorKey))));
        }

        Console.Write(
            $"{playlistName,-30} ");

        Console.Write(
            $"{tracks.Count,5} Tracks");

        var missing =
            files.Count - tracks.Count;

        if (missing > 0)
        {
            Console.ForegroundColor =
                ConsoleColor.Yellow;

            Console.Write(
                $"   {missing} nicht importiert");

            Console.ResetColor();
        }

        Console.WriteLine();

        return new XElement(
            "NODE",

            new XAttribute(
                "TYPE",
                "PLAYLIST"),

            new XAttribute(
                "NAME",
                playlistName),

            playlist);
    }

    // ---------------------------------------------------------
    // Saving
    // ---------------------------------------------------------

    private void WriteCollectionAtomically(XDocument doc)
    {
        // ---------------------------------------------------------
        // 1. Lokales Backup-Verzeichnis
        // ---------------------------------------------------------

        var backupDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "TraktorPlaylistGenerator",
                "Backups");

        Directory.CreateDirectory(backupDirectory);

        // ---------------------------------------------------------
        // 2. Backup-Dateinamen erzeugen
        // ---------------------------------------------------------

        var timestamp =
            DateTime.Now.ToString("yyyyMMdd_HHmmss");

        var machineName =
            Environment.MachineName;

        var backupPath =
            Path.Combine(
                backupDirectory,
                $"collection_{machineName}_{timestamp}.nml");

        Console.WriteLine();
        Console.WriteLine("Erstelle Backup...");

        // ---------------------------------------------------------
        // 3. Original-Collection sichern
        // ---------------------------------------------------------

        File.Copy(
            _options.CollectionPath,
            backupPath,
            overwrite: false);

        Console.WriteLine($"Backup: {backupPath}");

        // ---------------------------------------------------------
        // 4. Nur die letzten 5 Backups behalten
        // ---------------------------------------------------------

        CleanupOldBackups(
            backupDirectory,
            machineName,
            maxBackups: 5);

        // ---------------------------------------------------------
        // 5. Temporäre Datei erzeugen
        // ---------------------------------------------------------

        var tempPath =
            _options.CollectionPath +
            ".playlist-generator.tmp";

        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            Console.WriteLine(
                "Schreibe temporäre Collection...");

            // -----------------------------------------------------
            // 6. Collection zunächst als TMP speichern
            // -----------------------------------------------------

            doc.Save(
                tempPath,
                SaveOptions.DisableFormatting);

            // -----------------------------------------------------
            // 7. Datei prüfen
            // -----------------------------------------------------

            var tempFileInfo =
                new FileInfo(tempPath);

            if (!tempFileInfo.Exists ||
                tempFileInfo.Length == 0)
            {
                throw new InvalidOperationException(
                    "Die temporäre collection.nml wurde " +
                    "nicht korrekt geschrieben.");
            }

            // -----------------------------------------------------
            // 8. XML erneut laden
            // -----------------------------------------------------

            var validation =
                XDocument.Load(
                    tempPath,
                    LoadOptions.PreserveWhitespace);

            ValidateCollection(validation);

            // -----------------------------------------------------
            // 9. Traktor-Struktur prüfen
            // -----------------------------------------------------

            var collection =
                validation.Root?.Element("COLLECTION");

            var playlists =
                validation.Root?.Element("PLAYLISTS");

            if (collection == null)
            {
                throw new InvalidOperationException(
                    "Validierung fehlgeschlagen: " +
                    "COLLECTION fehlt.");
            }

            if (playlists == null)
            {
                throw new InvalidOperationException(
                    "Validierung fehlgeschlagen: " +
                    "PLAYLISTS fehlt.");
            }

            Console.WriteLine(
                "Temporäre Collection erfolgreich validiert.");

            // -----------------------------------------------------
            // 10. Original ersetzen
            // -----------------------------------------------------

            Console.WriteLine(
                "Ersetze collection.nml...");

            File.Move(
                tempPath,
                _options.CollectionPath,
                overwrite: true);

            // -----------------------------------------------------
            // 11. Erfolg
            // -----------------------------------------------------

            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Green;

            Console.WriteLine(
                "collection.nml erfolgreich aktualisiert.");

            Console.ResetColor();

            Console.WriteLine(
                $"Backup: {backupPath}");
        }
        catch
        {
            // -----------------------------------------------------
            // TMP-Datei bei Fehler entfernen
            // -----------------------------------------------------

            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ursprüngliche Exception nicht verdecken.
                }
            }

            Console.ForegroundColor =
                ConsoleColor.Red;

            Console.WriteLine();
            Console.WriteLine(
                "Schreiben der Collection fehlgeschlagen.");

            Console.WriteLine(
                "Die ursprüngliche collection.nml wurde " +
                "nach Möglichkeit nicht verändert.");

            Console.ResetColor();

            throw;
        }
    }
    
    private static void CleanupOldBackups(
        string backupDirectory,
        string machineName,
        int maxBackups)
    {
        var backups =
            Directory
                .EnumerateFiles(
                    backupDirectory,
                    $"collection_{machineName}_*.nml")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.CreationTimeUtc)
                .ToList();

        foreach (var oldBackup in backups.Skip(maxBackups))
        {
            try
            {
                oldBackup.Delete();

                Console.WriteLine(
                    $"Altes Backup gelöscht: {oldBackup.Name}");
            }
            catch (Exception ex)
            {
                // Ein nicht löschbares altes Backup soll nicht
                // verhindern, dass die Collection aktualisiert wird.
                Console.ForegroundColor =
                    ConsoleColor.Yellow;

                Console.WriteLine(
                    $"Altes Backup konnte nicht gelöscht werden: " +
                    $"{oldBackup.Name}");

                Console.WriteLine(
                    $"Grund: {ex.Message}");

                Console.ResetColor();
            }
        }
    }

    // ---------------------------------------------------------
    // Summary
    // ---------------------------------------------------------

    private void PrintSummary(int playlistCount)
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------");
        Console.WriteLine(
            $"Playlists:          {playlistCount}");

        Console.WriteLine(
            $"Fehlende Tracks:    {_missingTracks.Count}");

        if (_missingTracks.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine(
            "Nicht in Traktor importiert:");

        foreach (var track in
                 _missingTracks
                     .OrderBy(
                         x => x,
                         StringComparer.CurrentCultureIgnoreCase))
        {
            Console.WriteLine(
                $"  - {track}");
        }
    }
    
    private void PrintMissingTrackDiagnostics()
    {
        if (_missingTracks.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine(
            "Diagnose für fehlende Tracks:");
        Console.WriteLine();

        foreach (var missingPath in _missingTracks)
        {
            Console.ForegroundColor =
                ConsoleColor.Yellow;

            Console.WriteLine(
                $"FEHLEND: {missingPath}");

            Console.ResetColor();

            var missingDirectory =
                Path.GetDirectoryName(missingPath);

            if (string.IsNullOrWhiteSpace(
                    missingDirectory))
            {
                Console.WriteLine();
                continue;
            }

            var candidates =
                _collectionTracks
                    .Where(track =>
                    {
                        var directory =
                            Path.GetDirectoryName(
                                track.WindowsPath);

                        if (!string.Equals(
                                NormalizePath(directory ?? ""),
                                NormalizePath(missingDirectory),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        // Uns interessieren nur alte
                        // Collection-Einträge, deren Datei
                        // inzwischen nicht mehr existiert.
                        return !File.Exists(
                            track.WindowsPath);
                    })
                    .Select(track => new
                    {
                        Track = track,

                        Similarity =
                            ComparisonHelper.CalculateFileNameSimilarity(missingPath, track.WindowsPath)
                    })

                    // Nur halbwegs plausible Kandidaten.
                    .Where(x =>
                        x.Similarity >= 0.40)

                    .OrderByDescending(x =>
                        x.Similarity)

                    .Take(5)

                    .ToList();

            if (candidates.Count == 0)
            {
                Console.WriteLine(
                    "  Kein verwaister Collection-Eintrag im gleichen Ordner gefunden.");

                Console.WriteLine();
                continue;
            }

            Console.WriteLine(
                "  Möglicherweise umbenannt/ersetzt:");

            foreach (var candidate in candidates)
            {
                Console.WriteLine(
                    $"    {candidate.Track.WindowsPath}");

                Console.WriteLine(
                    $"    Ähnlichkeit: " +
                    $"{candidate.Similarity:P0}");
            }

            Console.WriteLine();
        }
    }
}