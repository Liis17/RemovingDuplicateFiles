using Spectre.Console;
using Spectre.Console.Cli;

namespace RemovingDuplicateFiles.CLI;

public sealed class ScanCommand : AsyncCommand<ScanSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ScanSettings settings)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            AnsiConsole.MarkupLine("[red]Получен Ctrl+C, останавливаемся...[/]");
        };
        var cancellationToken = cts.Token;

        var threads = settings.Threads ?? Environment.ProcessorCount;
        var logPath = settings.LogPath
            ?? Path.Combine(settings.TrashDir, $"move-log-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");

        AnsiConsole.Write(new Rule("[bold]Поиск дубликатов[/]").LeftJustified());
        AnsiConsole.MarkupLineInterpolated($"[grey]source:[/] {settings.SourceDir}");
        AnsiConsole.MarkupLineInterpolated($"[grey]target:[/] {settings.TargetDir}");
        AnsiConsole.MarkupLineInterpolated($"[grey]trash :[/] {settings.TrashDir}");
        AnsiConsole.Markup($"[grey]потоков:[/] {threads}");
        if (settings.DryRun)
            AnsiConsole.Markup("  [yellow](dry-run)[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();

        IReadOnlyList<DuplicateMatch> matches = Array.Empty<DuplicateMatch>();

        await AnsiConsole.Progress()
            .Columns(
                new SpinnerColumn(),
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn())
            .StartAsync(async ctx =>
            {
                var scanner = new DuplicateScanner(threads);
                matches = await scanner.FindAsync(settings.SourceDir, settings.TargetDir, ctx, cancellationToken);
            });

        AnsiConsole.WriteLine();

        if (matches.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Дубликаты не найдены.[/]");
            return 0;
        }

        RenderMatches(matches);

        if (settings.DryRun)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]Dry-run:[/] обнаружено {matches.Count} дубликатов, ничего не перемещалось.");
            return 0;
        }

        int moved = 0;
        await AnsiConsole.Progress()
            .Columns(
                new SpinnerColumn(),
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn())
            .StartAsync(ctx =>
            {
                var task = ctx.AddTask("[yellow]Перемещение в trash[/]", maxValue: matches.Count);
                var mover = new TrashMover(settings.TrashDir, logPath);
                moved = mover.MoveAll(matches, task, cancellationToken);
                return Task.CompletedTask;
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"[green]Готово.[/] Перемещено {moved} из {matches.Count}. Журнал: {logPath}");
        return 0;
    }

    private static void RenderMatches(IReadOnlyList<DuplicateMatch> matches)
    {
        long totalBytes = matches.Sum(m => m.Size);
        AnsiConsole.MarkupLineInterpolated(
            $"[bold]Найдено дубликатов:[/] {matches.Count} ([grey]~{FormatSize(totalBytes)}[/])");

        const int previewLimit = 10;
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[grey]Source[/]");
        table.AddColumn("[grey]Совпал с target[/]");
        table.AddColumn("[grey]Размер[/]");
        foreach (var m in matches.Take(previewLimit))
            table.AddRow(Markup.Escape(m.SourcePath), Markup.Escape(m.TargetPath), FormatSize(m.Size));
        AnsiConsole.Write(table);

        if (matches.Count > previewLimit)
            AnsiConsole.MarkupLineInterpolated($"[grey]... и ещё {matches.Count - previewLimit}. Полный список — в журнале.[/]");
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }
}
