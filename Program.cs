using System;
using System.Threading.Tasks;
using MatrixServer;
using Spectre.Console;

class Program
{
    private static ServerManager? _serverManager;

    static async Task Main(string[] args)
    {
        AnsiConsole.MarkupLine("[bold cyan]Matrix Server Manager (.NET)[/]");
        AnsiConsole.MarkupLine("[dim]macOS - PostgreSQL + Synapse + Nginx[/]\n");

        _serverManager = new ServerManager();

        while (true)
        {
            await DisplayMenu();
        }
    }

    static async Task DisplayMenu()
    {
        await _serverManager!.CheckStatusAsync();

        var statusColor = _serverManager.IsRunning ? "green" : "red";
        var statusText = _serverManager.IsRunning ? "RUNNING" : "STOPPED";
        AnsiConsole.MarkupLine($"[bold {statusColor}]Status: {statusText}[/]\n");

        AnsiConsole.MarkupLine("[yellow]Commands:[/]");
        AnsiConsole.MarkupLine("  [cyan]start[/]   - Start all services");
        AnsiConsole.MarkupLine("  [cyan]stop[/]    - Stop all services");
        AnsiConsole.MarkupLine("  [cyan]status[/]  - Check server status");
        AnsiConsole.MarkupLine("  [cyan]logs[/]    - View Synapse logs");
        AnsiConsole.MarkupLine("  [cyan]url[/]     - Show server URL");
        AnsiConsole.MarkupLine("  [cyan]dir[/]     - Show data directory");
        AnsiConsole.MarkupLine("  [cyan]exit[/]    - Exit application\n");

        var choice = AnsiConsole.Prompt(new TextPrompt<string>("[bold]Enter command:[/] ").PromptStyle("cyan"));

        switch (choice.ToLower())
        {
            case "start":
                await HandleStartServer();
                break;
            case "stop":
                await HandleStopServer();
                break;
            case "status":
                await HandleStatus();
                break;
            case "logs":
                await HandleViewLogs();
                break;
            case "url":
                AnsiConsole.MarkupLine($"[bold green]Server URL: {_serverManager.GetServerUrl()}[/]\n");
                break;
            case "dir":
                AnsiConsole.MarkupLine($"[bold cyan]Data Directory: {_serverManager.GetServerDirectory()}[/]\n");
                break;
            case "exit":
                AnsiConsole.MarkupLine("[yellow]Exiting...[/]");
                if (_serverManager.IsRunning)
                    await _serverManager.StopServerAsync();
                Environment.Exit(0);
                break;
            default:
                AnsiConsole.MarkupLine("[red]Unknown command[/]\n");
                break;
        }
    }

    static async Task HandleStartServer()
    {
        if (_serverManager!.IsRunning)
        {
            AnsiConsole.MarkupLine("[yellow]Server is already running[/]\n");
            return;
        }

        var spinner = new Progress.Spinner();
        var isSuccess = await _serverManager.StartServerAsync();

        if (isSuccess)
        {
            AnsiConsole.MarkupLine("[bold green]✓ Server started successfully![/]\n");
            AnsiConsole.MarkupLine($"[cyan]Access at: {_serverManager.GetServerUrl()}[/]\n");
        }
        else
        {
            AnsiConsole.MarkupLine("[bold red]✗ Failed to start server[/]\n");
        }
    }

    static async Task HandleStopServer()
    {
        if (!_serverManager!.IsRunning)
        {
            AnsiConsole.MarkupLine("[yellow]Server is not running[/]\n");
            return;
        }

        await _serverManager.StopServerAsync();
        AnsiConsole.MarkupLine("[bold green]✓ Server stopped[/]\n");
    }

    static async Task HandleStatus()
    {
        await _serverManager!.CheckStatusAsync();

        var statusColor = _serverManager.IsRunning ? "green" : "red";
        var statusText = _serverManager.IsRunning ? "RUNNING" : "STOPPED";

        AnsiConsole.MarkupLine($"[bold {statusColor}]Server Status: {statusText}[/]");
        AnsiConsole.MarkupLine($"[cyan]Server URL: {_serverManager.GetServerUrl()}[/]\n");
    }

    static async Task HandleViewLogs()
    {
        AnsiConsole.MarkupLine("[yellow]Fetching logs...[/]");
        var logs = await _serverManager!.GetLogsAsync();

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[bold cyan]=== Synapse Logs (Last 50 lines) ===[/]\n");
        AnsiConsole.MarkupLine(logs);
        AnsiConsole.MarkupLine("\n[dim]Press Enter to continue...[/]");
        Console.ReadLine();
        AnsiConsole.Clear();
    }
}

// Placeholder for Progress helper
public class Progress
{
    public class Spinner
    {
        // Used for visual feedback in the app
    }
}
