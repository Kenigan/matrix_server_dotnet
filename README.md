# Matrix Server Manager - C# .NET

A cross-platform console application for managing a Matrix homeserver (Synapse) with PostgreSQL and Nginx on macOS.

## Features

- **Service Management**: Start/stop PostgreSQL, Synapse, and Nginx from a single interface
- **Status Monitoring**: Check if services are running
- **Log Viewing**: View Synapse logs directly from the console
- **Auto-Configuration**: Automatically generates homeserver and Nginx configs
- **No Docker**: Uses native processes for better integration with macOS

## Prerequisites

- **.NET 8.0** or later
- **PostgreSQL 15** (installed via Homebrew)
- **Nginx** (installed via Homebrew)
- **Python 3.9+** (installed via Homebrew)
- **Matrix Synapse** (installed via pip)
- **macOS** (Arm64)

## Installation

1. Install dependencies:
```bash
brew install postgresql@15 nginx
pip install matrix-synapse[postgres]
```

2. Run setup script (from original Xcode project):
```bash
bash Scripts/setup.sh
```

3. Build the .NET application:
```bash
dotnet build
```

## Usage

Run the application:
```bash
dotnet run
```

### Commands

- **start** - Start all services (PostgreSQL, Synapse, Nginx)
- **stop** - Stop all services
- **status** - Check current server status
- **logs** - View last 50 lines of Synapse logs
- **url** - Display server URL
- **dir** - Show data directory path
- **exit** - Stop services and exit

## Configuration

The application stores its configuration in:
```
~/Library/Application Support/MatrixServer/
```

This includes:
- PostgreSQL data (`postgres_data/`)
- Synapse config (`synapse/config/`)
- Synapse data (`synapse/data/`)
- Media storage (`media/`)
- Nginx config (`nginx/`)

## Server Access

Once running, access your Matrix server at:
- **Web**: http://localhost:8008
- **API Endpoint**: http://localhost:8008/_matrix/

## Development

Build and run with debug output:
```bash
dotnet run --configuration Debug
```

## Dependencies

- **Spectre.Console**: Enhanced console output with colors and tables

## Architecture

- `ServerManager.cs` - Core service management logic
- `Program.cs` - Interactive console interface
- Services communicate via native process management

## Troubleshooting

### PostgreSQL won't start
Ensure PostgreSQL is installed: `brew list postgresql@15`

### Synapse not connecting to database
Verify PostgreSQL is running and check logs in `~/Library/Application Support/MatrixServer/synapse/logs/`

### Nginx errors
Check if port 80 is in use: `sudo lsof -i :80`

## Notes

- Hard-coded service paths are used (/opt/homebrew/Cellar/*) - update these if Homebrew versions change
- Application requires write access to ~/Library/Application Support/
- PostgreSQL data persists between runs
- Services run in background - use `stop` command to shut them down properly

## License

MIT
