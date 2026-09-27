namespace TraktorPlaylistGenerator;

public static class ComparisonHelper
{
    public static double CalculateFileNameSimilarity(
        string path1,
        string path2)
    {
        var name1 =
            Path.GetFileNameWithoutExtension(path1)
                .ToLowerInvariant();

        var name2 =
            Path.GetFileNameWithoutExtension(path2)
                .ToLowerInvariant();

        if (name1.Length == 0 &&
            name2.Length == 0)
        {
            return 1.0;
        }

        var distance =
            LevenshteinDistance(
                name1,
                name2);

        var maxLength =
            Math.Max(
                name1.Length,
                name2.Length);

        return 1.0 -
               (double)distance / maxLength;
    }

    private static int LevenshteinDistance(
        string source,
        string target)
    {
        var previous =
            new int[target.Length + 1];

        var current =
            new int[target.Length + 1];

        for (var j = 0;
             j <= target.Length;
             j++)
        {
            previous[j] = j;
        }

        for (var i = 1;
             i <= source.Length;
             i++)
        {
            current[0] = i;

            for (var j = 1;
                 j <= target.Length;
                 j++)
            {
                var cost =
                    source[i - 1] ==
                    target[j - 1]
                        ? 0
                        : 1;

                current[j] =
                    Math.Min(
                        Math.Min(
                            current[j - 1] + 1,
                            previous[j] + 1),
                        previous[j - 1] + cost);
            }

            (previous, current) =
                (current, previous);
        }

        return previous[target.Length];
    }
}