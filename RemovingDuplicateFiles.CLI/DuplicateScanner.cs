using System.Collections.Concurrent;
using System.IO.Hashing;
using Spectre.Console;

namespace RemovingDuplicateFiles.CLI;

public readonly record struct DuplicateMatch(string SourcePath, string TargetPath, long Size);

public sealed class DuplicateScanner
{
    private readonly int _maxParallelism;

    public DuplicateScanner(int maxParallelism)
    {
        _maxParallelism = maxParallelism;
    }

    public async Task<IReadOnlyList<DuplicateMatch>> FindAsync(
        string sourceDir,
        string targetDir,
        ProgressContext progress,
        CancellationToken ct)
    {
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        var enumTask = progress.AddTask("[grey]Сканирование папок[/]", maxValue: 2);
        var sourceFiles = EnumerateWithSize(sourceDir, enumerationOptions);
        enumTask.Increment(1);
        var targetFiles = EnumerateWithSize(targetDir, enumerationOptions);
        enumTask.Increment(1);

        // Группировка target по размеру; хэшируем только те файлы, у которых размер встречается в source.
        var targetsBySize = targetFiles
            .GroupBy(f => f.Size)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Path).ToArray());

        var sourcesBySize = sourceFiles
            .GroupBy(f => f.Size)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Path).ToArray());

        var sharedSizes = sourcesBySize.Keys.Intersect(targetsBySize.Keys).ToHashSet();

        var targetsToHash = targetsBySize
            .Where(kv => sharedSizes.Contains(kv.Key))
            .SelectMany(kv => kv.Value)
            .ToArray();

        var sourcesToHash = sourcesBySize
            .Where(kv => sharedSizes.Contains(kv.Key))
            .SelectMany(kv => kv.Value)
            .ToArray();

        AnsiConsole.MarkupLine(
            $"[grey]Найдено файлов:[/] source = {sourceFiles.Count}, target = {targetFiles.Count}. " +
            $"[grey]Кандидатов на хэширование:[/] source = {sourcesToHash.Length}, target = {targetsToHash.Length}.");

        if (sharedSizes.Count == 0)
            return Array.Empty<DuplicateMatch>();

        var targetHashTask = progress.AddTask("[yellow]Хэширование target[/]", maxValue: targetsToHash.Length);
        var targetHashes = await HashAllAsync(targetsToHash, targetHashTask, ct);

        // Хэш -> путь target. Если в target внутренние дубликаты, побеждает первый встретившийся.
        var targetByHash = new Dictionary<ulong, string>();
        foreach (var (path, hash) in targetHashes)
            targetByHash.TryAdd(hash, path);

        var sourceHashTask = progress.AddTask("[yellow]Хэширование source[/]", maxValue: sourcesToHash.Length);
        var sourceHashes = await HashAllAsync(sourcesToHash, sourceHashTask, ct);

        var sizeByPath = sourceFiles.ToDictionary(f => f.Path, f => f.Size);

        var matches = new List<DuplicateMatch>();
        foreach (var (sourcePath, sourceHash) in sourceHashes)
        {
            if (targetByHash.TryGetValue(sourceHash, out var targetPath))
                matches.Add(new DuplicateMatch(sourcePath, targetPath, sizeByPath[sourcePath]));
        }

        return matches;
    }

    private static List<(string Path, long Size)> EnumerateWithSize(string root, EnumerationOptions opts)
    {
        var result = new List<(string, long)>();
        foreach (var path in Directory.EnumerateFiles(root, "*", opts))
        {
            try
            {
                var size = new FileInfo(path).Length;
                result.Add((path, size));
            }
            catch (FileNotFoundException) { /* файл исчез между enumeration и stat — пропускаем */ }
            catch (UnauthorizedAccessException) { /* нет доступа — пропускаем */ }
        }
        return result;
    }

    private async Task<List<(string Path, ulong Hash)>> HashAllAsync(
        IReadOnlyList<string> files,
        ProgressTask progressTask,
        CancellationToken ct)
    {
        var results = new ConcurrentBag<(string, ulong)>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxParallelism,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(files, options, async (path, token) =>
        {
            try
            {
                var hash = await HashFileAsync(path, token);
                results.Add((path, hash));
            }
            catch (FileNotFoundException) { /* файл исчез — пропускаем */ }
            catch (UnauthorizedAccessException) { /* нет доступа — пропускаем */ }
            finally
            {
                progressTask.Increment(1);
            }
        });

        return results.ToList();
    }

    private static async Task<ulong> HashFileAsync(string path, CancellationToken ct)
    {
        var hasher = new XxHash64();
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1 << 16,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await hasher.AppendAsync(stream, ct);
        return hasher.GetCurrentHashAsUInt64();
    }
}
