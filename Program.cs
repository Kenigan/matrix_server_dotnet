using System;
using System.Diagnostics;
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
        AnsiConsole.MarkupLine("  [cyan]create[/]  - Create a new user");
        AnsiConsole.MarkupLine("  [cyan]list[/]    - List all users");
        AnsiConsole.MarkupLine("  [cyan]url[/]     - Show server URL");
        AnsiConsole.MarkupLine("  [cyan]dir[/]     - Show data directory");
        AnsiConsole.MarkupLine("  [cyan]disk[/]    - Show disk space usage");
        AnsiConsole.MarkupLine("  [cyan]config[/]  - Configure data path");
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
            case "disk":
                await HandleDiskUsage();
                break;
            case "create":
                await HandleCreateUser();
                break;
            case "list":
                await HandleListUsers();
                break;
            case "config":
                await HandleConfigurePath();
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

    static async Task HandleDiskUsage()
    {
        AnsiConsole.MarkupLine("[bold cyan]Calculating disk usage...[/]");
        
        var (bytes, formatted) = await _serverManager!.GetServerDiskUsageAsync();
        var dataDir = _serverManager.GetServerDirectory();
        
        AnsiConsole.MarkupLine($"[bold cyan]Server Data Directory: {dataDir}[/]");
        AnsiConsole.MarkupLine($"[bold green]Total Size: {formatted}[/]");
        AnsiConsole.MarkupLine($"[dim]({bytes:N0} bytes)[/]\n");
        
        // Open Finder window on macOS
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"open '{dataDir}'\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
            AnsiConsole.MarkupLine("[green]✓ Opened Finder window[/]\n");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ Could not open Finder: {ex.Message}[/]\n");
        }
    }

    static async Task HandleViewLogs()
    {
        AnsiConsole.MarkupLine("[yellow]Fetching logs...[/]");
        var logs = await _serverManager!.GetLogsAsync();

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[bold cyan]=== Synapse Logs (Last 50 lines) ===[/]\n");
        AnsiConsole.MarkupLine(Markup.Escape(logs));
        AnsiConsole.MarkupLine("\n[dim]Press Enter to continue...[/]");
        Console.ReadLine();
        AnsiConsole.Clear();
    }

    static async Task HandleCreateUser()
    {
        AnsiConsole.MarkupLine("[bold cyan]Create New Matrix User[/]\n");

        AnsiConsole.Markup("[bold]Username:[/] ");
        var username = Console.ReadLine()?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(username))
        {
            AnsiConsole.MarkupLine("[red]Username cannot be empty[/]\n");
            return;
        }

        AnsiConsole.Markup("[bold]Password:[/] ");
        var password = ReadPasswordFromConsole();

        if (string.IsNullOrWhiteSpace(password))
        {
            AnsiConsole.MarkupLine("[red]Password cannot be empty[/]\n");
            return;
        }

        AnsiConsole.MarkupLine("\n[yellow]Creating user...[/]");
        
        var (success, message) = await _serverManager!.CreateUserAsync(username, password);

        if (success)
        {
            AnsiConsole.MarkupLine($"[bold green]✓ {Markup.Escape(message)}[/]\n");
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold red]✗ {Markup.Escape(message)}[/]\n");
        }
    }

    static async Task HandleListUsers()
    {
        AnsiConsole.MarkupLine("[bold cyan]Matrix Server Users[/]\n");
        AnsiConsole.MarkupLine("[yellow]Fetching users...[/]");
        
        var (success, users, message) = await _serverManager!.ListUsersAsync();

        if (success && users.Count > 0)
        {
            AnsiConsole.MarkupLine($"[bold green]{Markup.Escape(message)}[/]\n");
            var table = new Table()
            {
                Border = TableBorder.Square
            };
            table.AddColumn("[cyan]Username[/]");
            foreach (var user in users)
            {
                table.AddRow(Markup.Escape(user));
            }
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("");
        }
        else if (success && users.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No users found[/]\n");
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold red]✗ {Markup.Escape(message)}[/]\n");
        }
    }

    static string ReadPasswordFromConsole()
    {
        var password = "";
        ConsoleKey key;
        do
        {
            var keyInfo = Console.ReadKey(true);
            key = keyInfo.Key;

            if (key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password = password.Substring(0, password.Length - 1);
                    Console.Write("\b \b");
                }
            }
            else if (key != ConsoleKey.Enter)
            {
                password += keyInfo.KeyChar;
                Console.Write("*");
            }
        } while (key != ConsoleKey.Enter);

        Console.WriteLine();
        return password;
    }

    static async Task HandleConfigurePath()
    {
        AnsiConsole.MarkupLine("[bold cyan]Current data directory:[/] " + Markup.Escape(_serverManager!.GetCurrentDataPath()) + "\n");

        AnsiConsole.Markup("[bold]Enter new data path (or leave blank to cancel):[/] ");
        var newPath = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(newPath))
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled[/]\n");
            return;
        }

        try
        {
            // Validate the path
            var fullPath = Path.GetFullPath(newPath);
            
            if (!Directory.Exists(fullPath))
            {
                var createDir = AnsiConsole.Confirm($"Directory does not exist. Create it at {fullPath}?");
                if (!createDir)
                {
                    AnsiConsole.MarkupLine("[yellow]Cancelled[/]\n");
                    return;
                }
                Directory.CreateDirectory(fullPath);
            }

            _serverManager.SetDataPath(fullPath);
            AnsiConsole.MarkupLine("[bold green]✓ Data path configured successfully![/]");
            AnsiConsole.MarkupLine("[yellow]Please restart the application for the change to take effect.[/]\n");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]✗ Error: {Markup.Escape(ex.Message)}[/]\n");
        }
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
