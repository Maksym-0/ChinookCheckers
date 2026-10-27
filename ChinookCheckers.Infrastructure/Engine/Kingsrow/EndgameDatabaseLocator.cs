using System.Text.RegularExpressions;

namespace ChinookCheckers.Infrastructure.Engine.Kingsrow;

/// <summary>Result of scanning a directory for the KingsRow WLD endgame database.</summary>
/// <param name="Directory">Directory that directly contains the db*.idx1 / db*.cpr1 files; this is what KingsRow's dbpath expects.</param>
/// <param name="MaxPieces">Largest piece count with a complete (index + data) file set.</param>
internal sealed record EndgameDatabaseInfo(string Directory, int MaxPieces, int FileCount, long SizeBytes);

/// <summary>
/// Locates the KingsRow English WLD database as produced by "Kingsrow_English_8pc_WLD_Setup.exe":
/// pairs of index/data files named <c>db{pieces}.idx1</c> + <c>db{pieces}.cpr1</c> for 2-7 pieces and
/// <c>db8-{subdivision}.idx1</c> + <c>.cpr1</c> for 8 pieces, installed into a <c>kr_english_wld</c> subfolder.
/// </summary>
internal static partial class EndgameDatabaseLocator
{
    private const string IndexExtension = ".idx1";
    private const string DataExtension = ".cpr1";
    private const int MinPieces = 2;
    private const int MaxSearchDepth = 3;

    /// <returns>Database info, or null when no usable (contiguous from 2 pieces) database was found.</returns>
    public static EndgameDatabaseInfo? Locate(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return null;
        }

        return EnumerateCandidateDirectories(rootDirectory, MaxSearchDepth)
            .Select(Inspect)
            .OfType<EndgameDatabaseInfo>()
            .MaxBy(info => info.MaxPieces);
    }

    private static IEnumerable<string> EnumerateCandidateDirectories(string directory, int depthLeft)
    {
        yield return directory;
        if (depthLeft == 0)
        {
            yield break;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            foreach (var nested in EnumerateCandidateDirectories(child, depthLeft - 1))
            {
                yield return nested;
            }
        }
    }

    private static EndgameDatabaseInfo? Inspect(string directory)
    {
        var piecesWithData = new SortedSet<int>();
        var fileCount = 0;
        long size = 0;

        foreach (var indexFile in Directory.EnumerateFiles(directory, "db*" + IndexExtension))
        {
            var match = FileNamePattern().Match(Path.GetFileName(indexFile));
            var dataFile = Path.ChangeExtension(indexFile, DataExtension);
            if (!match.Success || !File.Exists(dataFile))
            {
                continue;
            }

            piecesWithData.Add(int.Parse(match.Groups["pieces"].Value));
            fileCount += 2;
            size += new FileInfo(indexFile).Length + new FileInfo(dataFile).Length;
        }

        // Every size from 2 up to N must be present, otherwise probing would silently skip positions.
        var maxPieces = 0;
        foreach (var pieces in piecesWithData.Where(p => p >= MinPieces))
        {
            if (pieces != Math.Max(maxPieces, MinPieces - 1) + 1)
            {
                break;
            }

            maxPieces = pieces;
        }

        return maxPieces >= MinPieces ? new EndgameDatabaseInfo(directory, maxPieces, fileCount, size) : null;
    }

    [GeneratedRegex(@"^db(?<pieces>\d{1,2})(?:-\d+)?\.idx1$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();
}
