using Microsoft.Extensions.Logging;

namespace ChinookCheckers.Infrastructure.Engine.Kingsrow;

/// <summary>
/// Gives every worker its own DLL file: Windows loads a module once per path, so distinct file names are
/// the only way to get independent copies of KingsRow's global state inside one process.
/// Copies live next to the original because KingsRow resolves weights/book files relative to its module.
/// </summary>
internal sealed class KingsrowLibraryProvisioner(ILogger<KingsrowLibraryProvisioner> logger)
{
    private readonly Lock _workingDirectoryLock = new();

    /// <summary>
    /// KingsRow opens weights_*.bin and Kingsrow.odb relative to the process working directory and crashes
    /// (access violation) when they are missing, so the process must run from the engine directory.
    /// The working directory is process-wide, hence it is set once and never switched per call.
    /// </summary>
    public void EnsureWorkingDirectory(string sourcePath)
    {
        var engineDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        lock (_workingDirectoryLock)
        {
            if (string.Equals(
                    Path.TrimEndingDirectorySeparator(Environment.CurrentDirectory),
                    Path.TrimEndingDirectorySeparator(engineDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            logger.LogInformation(
                "Switching process working directory from {PreviousDirectory} to engine directory {EngineDirectory}",
                Environment.CurrentDirectory,
                engineDirectory);
            Environment.CurrentDirectory = engineDirectory;
        }
    }

    public string GetWorkerLibraryPath(string sourcePath, int workerId)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("KingsRow engine DLL not found.", sourcePath);
        }

        if (workerId == 0)
        {
            return sourcePath;
        }

        var directory = Path.GetDirectoryName(sourcePath)!;
        var targetPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(sourcePath)}.worker{workerId}{Path.GetExtension(sourcePath)}");

        if (IsUpToDate(sourcePath, targetPath))
        {
            return targetPath;
        }

        try
        {
            File.Copy(sourcePath, targetPath, overwrite: true);
            logger.LogInformation("Provisioned engine module {TargetPath} for worker {WorkerId}", targetPath, workerId);
        }
        catch (IOException ex) when (File.Exists(targetPath))
        {
            // During an IIS overlapped recycle the old process may still hold the copy loaded.
            logger.LogWarning(ex, "Engine module {TargetPath} is locked, reusing the existing copy", targetPath);
        }

        return targetPath;
    }

    private static bool IsUpToDate(string sourcePath, string targetPath)
    {
        var target = new FileInfo(targetPath);
        if (!target.Exists)
        {
            return false;
        }

        var source = new FileInfo(sourcePath);
        return source.Length == target.Length && source.LastWriteTimeUtc == target.LastWriteTimeUtc;
    }
}
