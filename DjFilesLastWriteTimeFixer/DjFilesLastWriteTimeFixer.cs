using System.Globalization;

namespace DjFilesLastWriteTimeFixer;

public sealed class DjFilesLastWriteTimeFixer
{
    private readonly string _djRoot;
    private readonly string _allesRoot;
    private readonly bool _dryRun;

    private readonly HashSet<string> _audioExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3",
            ".wav",
            ".aiff",
            ".aif",
            ".flac",
            ".m4a",
            ".ogg"
        };
    
    private readonly HashSet<string> _ignoredDuplicateFolders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "_FTE",
            "DancingKids"
        };

    public DjFilesLastWriteTimeFixer(
        string djRoot,
        string allesRoot,
        bool dryRun = true)
    {
        _djRoot = djRoot;
        _allesRoot = allesRoot;
        _dryRun = dryRun;
    }

    public void Run()
    {
        ValidatePaths();

        Console.WriteLine("======================================");
        Console.WriteLine(" DJ LastWriteTime Fixer");
        Console.WriteLine("======================================");
        Console.WriteLine();

        Console.WriteLine($"DJ:      {_djRoot}");
        Console.WriteLine($"Alles:   {_allesRoot}");
        Console.WriteLine($"Dry Run: {_dryRun}");
        Console.WriteLine();

        var allesIndex =
            BuildAllesIndex();

        var mappings =
            BuildMappings(allesIndex);

        var changes =
            FindChanges(mappings);

        PrintChanges(changes);

        if (_dryRun)
        {
            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Yellow;

            Console.WriteLine(
                "DRY RUN: Es wurde NICHTS verändert.");

            Console.WriteLine(
                "Wenn die Zuordnungen korrekt aussehen, " +
                "DryRun auf false setzen.");

            Console.ResetColor();

            return;
        }

        ApplyChanges(changes);

        VerifyChanges(changes);

        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Green;

        Console.WriteLine(
            $"{changes.Count:N0} LastWriteTime-Zeitstempel " +
            "erfolgreich wiederhergestellt.");

        Console.ResetColor();
    }

    private void ValidatePaths()
    {
        if (!Directory.Exists(_djRoot))
        {
            throw new DirectoryNotFoundException(
                $"DJ-Ordner nicht gefunden: {_djRoot}");
        }

        if (!Directory.Exists(_allesRoot))
        {
            throw new DirectoryNotFoundException(
                $"'Alles'-Ordner nicht gefunden: {_allesRoot}");
        }
    }

    private Dictionary<string, List<string>>
        BuildAllesIndex()
    {
        Console.WriteLine(
            "Indexiere Referenzdateien aus 'Alles'...");

        var index =
            new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);

        var count = 0;

        foreach (var path in Directory.EnumerateFiles(
                     _allesRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (!IsAudioFile(path))
                continue;

            count++;

            var fileName =
                Path.GetFileName(path);

            if (!index.TryGetValue(
                    fileName,
                    out var files))
            {
                files = [];
                index[fileName] = files;
            }

            files.Add(path);
        }

        Console.WriteLine(
            $"{count:N0} Referenzdateien gefunden.");

        return index;
    }

    private List<FileMapping> BuildMappings(
        Dictionary<string, List<string>> allesIndex)
    {
        Console.WriteLine();
        Console.WriteLine(
            "Prüfe Zuordnung der DJ-Dateien...");

        var mappings =
            new List<FileMapping>();

        var errors =
            new List<string>();

        var djCount = 0;
        var ignoredDuplicates = 0;
        var duplicateFolderUsedAsSource = 0;

        foreach (var djPath in Directory.EnumerateFiles(
                     _djRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (!IsAudioFile(djPath))
                continue;

            djCount++;

            var fileName =
                Path.GetFileName(djPath);

            if (!allesIndex.TryGetValue(
                    fileName,
                    out var matches))
            {
                errors.Add(
                    $"NICHT GEFUNDEN:\n" +
                    $"  {djPath}");

                continue;
            }

            var normalMatches =
                matches
                    .Where(path =>
                        !IsInIgnoredDuplicateFolder(path))
                    .ToList();

            var duplicateFolderMatches =
                matches
                    .Where(IsInIgnoredDuplicateFolder)
                    .ToList();

            string? selectedMatch = null;

            // ----------------------------------------------------
            // Fall 1:
            // Genau ein normaler Treffer.
            //
            // Dieser hat Vorrang. Eventuelle Treffer in _FTE
            // werden als Duplikate ignoriert.
            // ----------------------------------------------------

            if (normalMatches.Count == 1)
            {
                selectedMatch =
                    normalMatches[0];

                ignoredDuplicates +=
                    duplicateFolderMatches.Count;
            }

            // ----------------------------------------------------
            // Fall 2:
            // Kein normaler Treffer, aber genau EIN Treffer
            // im DuplicateFolder.
            //
            // Dann darf dieser als Referenz verwendet werden.
            // ----------------------------------------------------

            else if (normalMatches.Count == 0 &&
                     duplicateFolderMatches.Count == 1)
            {
                selectedMatch =
                    duplicateFolderMatches[0];

                duplicateFolderUsedAsSource++;
            }

            // ----------------------------------------------------
            // Alles andere ist nicht eindeutig.
            // ----------------------------------------------------

            if (selectedMatch == null)
            {
                var normalText =
                    normalMatches.Count == 0
                        ? "    <keine>"
                        : string.Join(
                            Environment.NewLine,
                            normalMatches.Select(
                                x => $"    {x}"));

                var duplicateText =
                    duplicateFolderMatches.Count == 0
                        ? "    <keine>"
                        : string.Join(
                            Environment.NewLine,
                            duplicateFolderMatches.Select(
                                x => $"    {x}"));

                errors.Add(
                    $"NICHT EINDEUTIG:\n" +
                    $"  DJ: {djPath}\n" +
                    $"  Normale Treffer:\n" +
                    $"{normalText}\n" +
                    $"  Treffer in DuplicateFolder:\n" +
                    $"{duplicateText}");

                continue;
            }

            mappings.Add(
                new FileMapping(
                    djPath,
                    selectedMatch));
        }

        // --------------------------------------------------------
        // Erst NACH Prüfung aller Dateien abbrechen.
        // Bis hierhin wurde noch kein Zeitstempel verändert.
        // --------------------------------------------------------

        if (errors.Count > 0)
        {
            PrintMappingErrors(errors);

            throw new InvalidOperationException(
                $"{errors.Count:N0} DJ-Datei(en) konnten " +
                "nicht eindeutig zugeordnet werden. " +
                "Es wurde nichts verändert.");
        }

        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Green;

        Console.WriteLine(
            $"Alle {djCount:N0} DJ-Dateien wurden " +
            "eindeutig in 'Alles' gefunden.");

        Console.ResetColor();

        if (ignoredDuplicates > 0)
        {
            Console.WriteLine(
                $"{ignoredDuplicates:N0} zusätzliche Treffer " +
                "in DuplicateFoldern wurden ignoriert.");
        }

        if (duplicateFolderUsedAsSource > 0)
        {
            Console.WriteLine(
                $"{duplicateFolderUsedAsSource:N0} Datei(en) " +
                "wurden ausschließlich in einem " +
                "DuplicateFolder gefunden und als " +
                "Referenz verwendet.");
        }

        return mappings;
    }

    private static List<TimeChange> FindChanges(
        IEnumerable<FileMapping> mappings)
    {
        var changes =
            new List<TimeChange>();

        foreach (var mapping in mappings)
        {
            var djTime =
                File.GetLastWriteTimeUtc(
                    mapping.DjPath);

            var referenceTime =
                File.GetLastWriteTimeUtc(
                    mapping.ReferencePath);

            if (djTime == referenceTime)
                continue;

            changes.Add(
                new TimeChange(
                    mapping.DjPath,
                    mapping.ReferencePath,
                    djTime,
                    referenceTime));
        }

        return changes;
    }

    private static void PrintChanges(
        IReadOnlyCollection<TimeChange> changes)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"{changes.Count:N0} unterschiedliche Zeitstempel:");

        Console.WriteLine();

        foreach (var change in changes)
        {
            Console.WriteLine(
                change.DjPath);

            Console.WriteLine(
                $"  Quelle: {change.ReferencePath}");

            Console.WriteLine(
                $"  Alt:    {FormatTime(change.OldTimeUtc)}");

            Console.WriteLine(
                $"  Neu:    {FormatTime(change.NewTimeUtc)}");

            Console.WriteLine();
        }
    }

    private static void ApplyChanges(
        IEnumerable<TimeChange> changes)
    {
        Console.WriteLine(
            "Stelle LastWriteTime wieder her...");

        foreach (var change in changes)
        {
            File.SetLastWriteTimeUtc(
                change.DjPath,
                change.NewTimeUtc);
        }
    }

    private static void VerifyChanges(
        IEnumerable<TimeChange> changes)
    {
        var errors =
            new List<string>();

        foreach (var change in changes)
        {
            var actual =
                File.GetLastWriteTimeUtc(
                    change.DjPath);

            if (actual != change.NewTimeUtc)
            {
                errors.Add(
                    change.DjPath);
            }
        }

        if (errors.Count == 0)
            return;

        var message =
            "Einige Zeitstempel konnten nicht " +
            "korrekt gesetzt werden:\n\n" +
            string.Join(
                Environment.NewLine,
                errors);

        throw new InvalidOperationException(
            message);
    }
    
    private bool IsInIgnoredDuplicateFolder(
        string filePath)
    {
        var relativePath =
            Path.GetRelativePath(
                _allesRoot,
                filePath);

        // Ersten Bestandteil des relativen Pfades bestimmen.
        var firstSeparatorIndex =
            relativePath.IndexOfAny(
            [
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            ]);

        // Datei liegt direkt in "Alles".
        if (firstSeparatorIndex < 0)
            return false;

        var firstFolder =
            relativePath[..firstSeparatorIndex];

        return _ignoredDuplicateFolders.Contains(
            firstFolder);
    }

    private static void PrintMappingErrors(
        IEnumerable<string> errors)
    {
        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Red;

        Console.WriteLine("ABBRUCH!");
        Console.WriteLine();

        Console.WriteLine(
            "Nicht alle DJ-Dateien konnten eindeutig " +
            "zugeordnet werden.");

        Console.WriteLine(
            "Es wurde NICHTS verändert.");

        Console.ResetColor();

        Console.WriteLine();

        foreach (var error in errors)
        {
            Console.WriteLine(error);
            Console.WriteLine();
        }
    }

    private bool IsAudioFile(
        string path)
    {
        return _audioExtensions.Contains(
            Path.GetExtension(path));
    }

    private static string FormatTime(
        DateTime utc)
    {
        return utc
            .ToLocalTime()
            .ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture);
    }

    private sealed record FileMapping(
        string DjPath,
        string ReferencePath);

    private sealed record TimeChange(
        string DjPath,
        string ReferencePath,
        DateTime OldTimeUtc,
        DateTime NewTimeUtc);
}