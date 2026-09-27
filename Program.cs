using System.Text;
using TraktorPlaylistGenerator;

Console.OutputEncoding = Encoding.UTF8;

var options = new TraktorPlaylistOptions
{
    MusicRoot = @"V:\Audio\Musik\DJ",

    TraktorDataFolder = "_TraktorData",

    PlaylistFolderName = "Folder Playlists",

    IgnoredFolders =
    [
        "_Playlists",
        ".stfolder"
    ],

    // Beim ersten Lauf true, danach false
    DryRun = false
};

try
{
    var generator = new TraktorPlaylistGenerator.TraktorPlaylistGenerator(options);
    generator.Generate();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;

    Console.WriteLine();
    Console.WriteLine("FEHLER:");
    Console.WriteLine(ex.Message);

    Console.ResetColor();

    Environment.ExitCode = 1;
}