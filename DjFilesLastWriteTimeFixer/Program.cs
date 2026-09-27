using System.Text;

namespace DjFilesLastWriteTimeFixer;

internal static class Program
{
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        const string djRoot =
            @"V:\Audio\Musik\DJ";

        const string allesRoot =
            @"V:\Audio\Musik\Alles";

        // Erst mit true testen!
        const bool dryRun = true;

        try
        {
            var fixer =
                new DjFilesLastWriteTimeFixer(
                    djRoot,
                    allesRoot,
                    dryRun);

            fixer.Run();

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Red;

            Console.WriteLine("FEHLER:");
            Console.WriteLine(ex.Message);

            Console.ResetColor();

            return 1;
        }
    }
}
