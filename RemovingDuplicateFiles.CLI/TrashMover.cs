using System.Text.Json;
using Spectre.Console;

namespace RemovingDuplicateFiles.CLI;

public sealed class TrashMover
{
    private readonly string _trashDir;
    private readonly string _logPath;

    public TrashMover(string trashDir, string logPath)
    {
        _trashDir = trashDir;
        _logPath = logPath;
    }

    public int MoveAll(IReadOnlyList<DuplicateMatch> matches, ProgressTask task, CancellationToken ct)
    {
        Directory.CreateDirectory(_trashDir);
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);

        int moved = 0;
        using var logStream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var logWriter = new StreamWriter(logStream);

        foreach (var match in matches)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var dest = ResolveDestination(match.SourcePath);
                MoveCrossVolume(match.SourcePath, dest);

                var entry = new MoveLogEntry(
                    DateTimeOffset.UtcNow,
                    match.SourcePath,
                    dest,
                    match.TargetPath,
                    match.Size);
                logWriter.WriteLine(JsonSerializer.Serialize(entry));
                moved++;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Не удалось переместить[/] {match.SourcePath}: {ex.Message}");
            }
            finally
            {
                task.Increment(1);
            }
        }

        return moved;
    }

    private string ResolveDestination(string sourcePath)
    {
        var name = Path.GetFileName(sourcePath);
        var candidate = Path.Combine(_trashDir, name);
        if (!File.Exists(candidate))
            return candidate;

        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        return Path.Combine(_trashDir, $"{stem}_{Guid.NewGuid():N}{ext}");
    }

    private static void MoveCrossVolume(string source, string dest)
    {
        try
        {
            File.Move(source, dest);
        }
        catch (IOException) when (!SameVolume(source, dest))
        {
            // Файлы на разных томах — File.Move на старых рантаймах падал; делаем copy + delete как fallback.
            File.Copy(source, dest, overwrite: false);
            File.Delete(source);
        }
    }

    private static bool SameVolume(string a, string b)
    {
        var rootA = Path.GetPathRoot(Path.GetFullPath(a));
        var rootB = Path.GetPathRoot(Path.GetFullPath(b));
        return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record MoveLogEntry(
        DateTimeOffset MovedAtUtc,
        string From,
        string To,
        string MatchedTarget,
        long Size);
}
