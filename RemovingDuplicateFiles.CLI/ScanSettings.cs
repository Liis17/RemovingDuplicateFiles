using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace RemovingDuplicateFiles.CLI;

public sealed class ScanSettings : CommandSettings
{
    [CommandOption("-s|--source <PATH>")]
    [Description("Папка, в которой ищутся дубликаты (из неё файлы перемещаются).")]
    public string SourceDir { get; set; } = string.Empty;

    [CommandOption("-t|--target <PATH>")]
    [Description("Эталонная папка для сравнения (альбомы). Не изменяется.")]
    public string TargetDir { get; set; } = string.Empty;

    [CommandOption("--trash <PATH>")]
    [Description("Куда перемещать найденные дубликаты.")]
    public string TrashDir { get; set; } = string.Empty;

    [CommandOption("--dry-run")]
    [Description("Только показать дубликаты, ничего не перемещать.")]
    public bool DryRun { get; set; }

    [CommandOption("--threads <N>")]
    [Description("Сколько потоков использовать для хэширования. По умолчанию — число логических ядер.")]
    public int? Threads { get; set; }

    [CommandOption("--log <PATH>")]
    [Description("Куда писать журнал перемещений (JSONL). По умолчанию — в папку --trash.")]
    public string? LogPath { get; set; }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceDir))
            return ValidationResult.Error("--source не задан.");
        if (string.IsNullOrWhiteSpace(TargetDir))
            return ValidationResult.Error("--target не задан.");
        if (string.IsNullOrWhiteSpace(TrashDir))
            return ValidationResult.Error("--trash не задан.");

        SourceDir = Path.GetFullPath(SourceDir);
        TargetDir = Path.GetFullPath(TargetDir);
        TrashDir = Path.GetFullPath(TrashDir);

        if (!Directory.Exists(SourceDir))
            return ValidationResult.Error($"Папка --source не существует: {SourceDir}");
        if (!Directory.Exists(TargetDir))
            return ValidationResult.Error($"Папка --target не существует: {TargetDir}");

        if (PathsOverlap(SourceDir, TargetDir))
            return ValidationResult.Error("--source и --target не должны совпадать или быть вложены друг в друга.");
        if (PathsOverlap(TrashDir, SourceDir) || PathsOverlap(TrashDir, TargetDir))
            return ValidationResult.Error("--trash не должна быть внутри --source или --target (и наоборот).");

        if (Threads is <= 0)
            return ValidationResult.Error("--threads должно быть положительным числом.");

        return ValidationResult.Success();
    }

    private static bool PathsOverlap(string a, string b)
    {
        var na = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
        var nb = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
        if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase))
            return true;
        var sep = Path.DirectorySeparatorChar;
        return na.StartsWith(nb + sep, StringComparison.OrdinalIgnoreCase)
            || nb.StartsWith(na + sep, StringComparison.OrdinalIgnoreCase);
    }
}
