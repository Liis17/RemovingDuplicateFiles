using System.Text;
using RemovingDuplicateFiles.CLI;
using Spectre.Console.Cli;

Console.OutputEncoding = Encoding.UTF8;

var app = new CommandApp<ScanCommand>();
app.Configure(config =>
{
    config.SetApplicationName("dedup");
    config.AddExample(["--source", @"C:\Photos", "--target", @"C:\Albums", "--trash", @"C:\Trash"]);
    config.AddExample(["-s", @"C:\Photos", "-t", @"C:\Albums", "--trash", @"C:\Trash", "--dry-run"]);
});

return await app.RunAsync(args);
