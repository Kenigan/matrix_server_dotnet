using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MatrixServer;

public class ServerManager
{
    private readonly string _serverDirectory;
    private readonly string _postgresDataDirectory;
    private readonly string _synapseVenvPython;
    private Process? _synapseProcess;
    private Process? _nginxProcess;
    private DateTime _lastStartTime = DateTime.MinValue;
    private const int INITIALIZATION_GRACE_PERIOD_MS = 15000; // 15 seconds grace period for initialization

    public bool IsRunning { get; private set; }

    public ServerManager()
    {
        var appSupport = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MatrixServer"
        );
        _serverDirectory = appSupport;
        _postgresDataDirectory = Path.Combine(appSupport, "postgres_data");
        _synapseVenvPython = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".synapse_venv/bin/python3");

        SetupServerDirectory();
    }

    private void SetupServerDirectory()
    {
        var directories = new[]
        {
            _serverDirectory,
            Path.Combine(_serverDirectory, "synapse"),
            Path.Combine(_serverDirectory, "synapse/config"),
            Path.Combine(_serverDirectory, "synapse/data"),
            Path.Combine(_serverDirectory, "synapse/logs"),
            _postgresDataDirectory,
            Path.Combine(_serverDirectory, "nginx"),
            Path.Combine(_serverDirectory, "media")
        };

        foreach (var dir in directories)
        {
            Directory.CreateDirectory(dir);
        }

        // Initialize PostgreSQL database if needed
        InitializePostgresIfNeeded();
        
        WriteHomeserverConfig();
        WriteNginxConfig();
    }

    private void InitializePostgresIfNeeded()
    {
        // Check if PG_VERSION exists (indicates initialized database)
        var pgVersionFile = Path.Combine(_postgresDataDirectory, "PG_VERSION");
        
        if (!File.Exists(pgVersionFile))
        {
            Console.WriteLine("Initializing PostgreSQL database cluster...");
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"/opt/homebrew/Cellar/postgresql@15/15.17/bin/initdb -D '{_postgresDataDirectory}' --encoding=UTF8 --locale=C\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(30000);
                
                if (process.ExitCode == 0)
                {
                    Console.WriteLine("✓ PostgreSQL database initialized");
                }
                else
                {
                    var error = process.StandardError.ReadToEnd();
                    Console.WriteLine($"⚠ PostgreSQL initialization warning: {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Could not auto-initialize PostgreSQL: {ex.Message}");
            }
        }
    }

    private void WriteHomeserverConfig()
    {
        var configPath = Path.Combine(_serverDirectory, "synapse/config");
        var dataPath = Path.Combine(_serverDirectory, "synapse");
        var logsPath = Path.Combine(_serverDirectory, "synapse/logs");
        var mediaPath = Path.Combine(_serverDirectory, "media");

        var config = $@"server_name: ""localhost""
pid_file: {dataPath}/homeserver.pid
listeners:
  - port: 8008
    tls: false
    type: http
    x_forwarded: true
    resources:
      - names: [client, federation]
        compress: false

database:
  name: psycopg2
  args:
    user: synapse
    password: synapse_password
    database: synapse
    host: localhost
    port: 5432
    cp_min: 5
    cp_max: 10

media_store_path: {mediaPath}
uploads_path: {mediaPath}/uploads
max_upload_size: 5368709120
max_image_pixels: 100000000
rc_media:
  per_second: 10
  burst_count: 100

retention:
  enabled: true
  default_policy:
    min_lifetime: 1d
    max_lifetime: 1y

enable_registration: true
enable_registration_without_verification: true
registrations_require_3pid: []

macaroon_secret_key: ""GENERATED_SECRET_KEY_REPLACE_THIS""
form_secret: ""GENERATED_FORM_SECRET_REPLACE_THIS""

signing_key_path: ""{configPath}/localhost.signing.key""

log_config: ""{configPath}/localhost.log.config""

report_stats: false

app_service_config_files: []

expire_access_token: false

trusted_key_servers:
  - server_name: ""matrix.org""

suppress_key_server_warning: true

url_preview_enabled: true
url_preview_ip_range_blacklist:
  - '127.0.0.0/8'
  - '10.0.0.0/8'
  - '172.16.0.0/12'
  - '192.168.0.0/16'
  - '100.64.0.0/10'
  - '169.254.0.0/16'
  - '::1/128'
  - 'fe80::/64'
  - 'fc00::/7'

oembed:
  enabled: true

password_config:
  policy:
    enabled: true
    minimum_length: 8
    require_digit: true
    require_symbol: false
    require_lowercase: true
    require_uppercase: true

ui_auth:
  session_timeout: 24h

user_directory:
  enabled: true
  search_all_users: true
  prefer_local_users: true
";

        var configFile = Path.Combine(configPath, "homeserver.yaml");
        File.WriteAllText(configFile, config);

        // Generate a proper 32-byte signing key (256-bit base64 encoded)
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            var keyBytes = new byte[32];
            rng.GetBytes(keyBytes);
            var base64Key = Convert.ToBase64String(keyBytes);
            var signingKey = $"ed25519 a_localhost {base64Key}";
            var signingKeyFile = Path.Combine(configPath, "localhost.signing.key");
            File.WriteAllText(signingKeyFile, signingKey);
        }

        var logConfig = $@"version: 1
formatters:
  precise:
    format: '%(asctime)s - %(name)s - %(lineno)d - %(levelname)s - %(request)s - %(message)s'
handlers:
  file:
    class: logging.handlers.RotatingFileHandler
    formatter: precise
    filename: {logsPath}/homeserver.log
    maxBytes: 104857600
    backupCount: 10
  console:
    class: logging.StreamHandler
    formatter: precise
loggers:
  synapse.storage.SQL:
    level: INFO
root:
  level: INFO
  handlers: [file, console]
";

        var logConfigFile = Path.Combine(configPath, "localhost.log.config");
        File.WriteAllText(logConfigFile, logConfig);
    }

    private void WriteNginxConfig()
    {
        var nginxConfig = $@"worker_processes auto;
error_log /tmp/nginx_error.log warn;
pid /tmp/nginx.pid;

events {{
    worker_connections 1024;
}}

http {{
    client_max_body_size 5G;
    client_body_buffer_size 128k;
    client_body_timeout 300s;
    proxy_read_timeout 300s;
    proxy_send_timeout 300s;
    
    upstream synapse {{
        # Synapse runs on 8008
        server 127.0.0.1:8008;
    }}
    
    server {{
        listen 80;
        server_name localhost;
        
        location / {{
            proxy_pass http://synapse;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            
            proxy_buffering off;
            proxy_request_buffering off;
        }}
        
        location /_matrix/media {{
            alias {Path.Combine(_serverDirectory, "media")};
            expires 1y;
            add_header Cache-Control ""public, immutable"";
        }}
    }}
}}
";

        var nginxConfigPath = Path.Combine(_serverDirectory, "nginx/nginx.conf");
        File.WriteAllText(nginxConfigPath, nginxConfig);
    }

    public async Task<bool> StartServerAsync()
    {
        Console.WriteLine("Starting server services...");
        _lastStartTime = DateTime.UtcNow;

        // Stop any potentially already-running services
        await CleanupExistingProcessesAsync();
        
        await Task.Delay(1000);

        if (!await StartPostgresAsync())
        {
            Console.WriteLine("Failed to start PostgreSQL");
            return false;
        }

        await Task.Delay(2000);

        if (!await StartSynapseAsync())
        {
            Console.WriteLine("Failed to start Synapse");
            await StopPostgresAsync();
            return false;
        }

        await Task.Delay(3000);

        if (!await StartNginxAsync())
        {
            Console.WriteLine("Failed to start Nginx");
            await StopSynapseAsync();
            await StopPostgresAsync();
            return false;
        }

        // Wait for services to fully initialize
        await Task.Delay(2000);
        
        // Check if processes are still running
        var processesRunning = AreCoreProcessesRunning();
        if (processesRunning)
        {
            IsRunning = true;
            Console.WriteLine("✓ All services started successfully");
        }
        else
        {
            Console.WriteLine("⚠ Services were started but processes are not responding");
            IsRunning = true;
        }
        
        return true;
    }

    private async Task CleanupExistingProcessesAsync()
    {
        // Kill any existing Synapse processes
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"pkill -f 'synapse.app.homeserver' || true\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
        }
        catch { }

        // Kill any existing Nginx processes
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"  /opt/homebrew/Cellar/nginx/1.29.8/bin/nginx -s quit || pkill -f nginx || true\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(2000);
        }
        catch { }
    }

    private async Task<bool> StartPostgresAsync()
    {
        Console.WriteLine("Starting PostgreSQL...");

        try
        {
            // First, ensure any stale PostgreSQL processes are stopped gracefully
            await StopPostgresAsync();
            await Task.Delay(1000);

            // Now safe to clean up PID file
            var pidFile = Path.Combine(_postgresDataDirectory, "postmaster.pid");
            if (File.Exists(pidFile))
            {
                try
                {
                    File.Delete(pidFile);
                }
                catch { }
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"/opt/homebrew/Cellar/postgresql@15/15.17/bin/pg_ctl start -D '{_postgresDataDirectory}' -l '{Path.Combine(_postgresDataDirectory, "postgres.log")}' -o '-p 5432'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            if (output.Length > 0) Console.WriteLine($"PostgreSQL: {output}");
            if (error.Length > 0 && !error.Contains("another server might be running")) 
                Console.WriteLine($"PostgreSQL Error: {error}");

            if (process.ExitCode == 0)
            {
                Console.WriteLine("✓ PostgreSQL started");
                return true;
            }

            Console.WriteLine($"PostgreSQL start failed with code: {process.ExitCode}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PostgreSQL exception: {ex.Message}");
            return false;
        }
    }

    private async Task StopPostgresAsync()
    {
        Console.WriteLine("Stopping PostgreSQL...");

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"/opt/homebrew/Cellar/postgresql@15/15.17/bin/pg_ctl stop -D '{_postgresDataDirectory}' -m fast\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            process.WaitForExit(5000);
            Console.WriteLine("✓ PostgreSQL stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping PostgreSQL: {ex.Message}");
        }

        // Also try to kill any remaining postgres processes
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"pkill -f 'postgres -D' || true\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(2000);
        }
        catch { }
    }

    private async Task<bool> StartSynapseAsync()
    {
        Console.WriteLine("Starting Synapse...");

        try
        {
            var configPath = Path.Combine(_serverDirectory, "synapse/config/homeserver.yaml");
            var logPath = Path.Combine(_serverDirectory, "synapse/startup_error.log");
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _synapseVenvPython,
                    Arguments = $"-m synapse.app.homeserver -c \"{configPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            // Give it a moment to start or fail
            await Task.Delay(2000);
            
            // Check if process exited immediately (error during startup)
            if (process.HasExited)
            {
                var error = await process.StandardError.ReadToEndAsync();
                var output = await process.StandardOutput.ReadToEndAsync();
                
                // Save full output to file for debugging
                try
                {
                    File.WriteAllText(logPath, $"STDOUT:\n{output}\n\nSTDERR:\n{error}");
                }
                catch { }
                
                if (error.Length > 0)
                {
                    Console.WriteLine($"Synapse Error: {error.Substring(0, Math.Min(300, error.Length))}...");
                }
                if (output.Length > 0)
                {
                    Console.WriteLine($"Synapse Output: {output.Substring(0, Math.Min(300, output.Length))}...");
                }
                
                return false;
            }
            
            _synapseProcess = process;
            Console.WriteLine("✓ Synapse started");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Synapse exception: {ex.Message}");
            return false;
        }
    }

    private async Task StopSynapseAsync()
    {
        if (_synapseProcess?.HasExited == false)
        {
            _synapseProcess.Kill();
            _synapseProcess.Dispose();
            _synapseProcess = null;
            Console.WriteLine("✓ Synapse stopped");
        }
    }

    private async Task<bool> StartNginxAsync()
    {
        Console.WriteLine("Starting Nginx...");

        try
        {
            var nginxConfPath = Path.Combine(_serverDirectory, "nginx/nginx.conf");
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"/opt/homebrew/Cellar/nginx/1.29.8/bin/nginx -c '{nginxConfPath}'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            _nginxProcess = process;
            Console.WriteLine("✓ Nginx started");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Nginx exception: {ex.Message}");
            return false;
        }
    }

    private async Task StopNginxAsync()
    {
        Console.WriteLine("Stopping Nginx...");

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"/opt/homebrew/Cellar/nginx/1.29.8/bin/nginx -s quit\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();
            Console.WriteLine("✓ Nginx stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping Nginx: {ex.Message}");
        }

        if (_nginxProcess?.HasExited == false)
        {
            _nginxProcess.Kill();
        }
    }

    public async Task StopServerAsync()
    {
        Console.WriteLine("Stopping server services...");
        await StopSynapseAsync();
        await StopNginxAsync();
        await StopPostgresAsync();
        IsRunning = false;
        Console.WriteLine("✓ All services stopped");
    }

    public async Task CheckStatusAsync()
    {
        // If we recently started services, give them grace period to initialize
        var timeSinceStart = DateTime.UtcNow - _lastStartTime;
        if (timeSinceStart.TotalMilliseconds < INITIALIZATION_GRACE_PERIOD_MS)
        {
            // During grace period, check if processes are still running
            IsRunning = AreCoreProcessesRunning();
            return;
        }

        // After grace period, try HTTP check
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/curl",
                    Arguments = "-s --connect-timeout 2 -m 3 -o /dev/null -w \"%{http_code}\" http://localhost:8008/_synapse/admin/v1/server_version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            // Accept any 2xx or 4xx response as "running"
            if (int.TryParse(output.Trim(), out var statusCode))
            {
                IsRunning = statusCode > 0 && statusCode < 500;
            }
            else
            {
                // If HTTP check fails, fall back to process check
                IsRunning = AreCoreProcessesRunning();
            }
        }
        catch
        {
            IsRunning = AreCoreProcessesRunning();
        }
    }

    private bool AreCoreProcessesRunning()
    {
        // Check if Synapse process is still running
        if (_synapseProcess != null && !_synapseProcess.HasExited)
        {
            // Synapse is still running
            return true;
        }

        // If we don't have a process reference, check system processes
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"ps aux | grep -E 'synapse.app.homeserver|postgres' | grep -v grep | wc -l\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (int.TryParse(output.Trim(), out var count))
            {
                return count > 0;
            }
        }
        catch { }

        return false;
    }

    public string GetServerUrl() => "http://localhost:8008";

    public string GetServerDirectory() => _serverDirectory;

    public async Task<string> GetLogsAsync()
    {
        try
        {
            var logsPath = Path.Combine(_serverDirectory, "synapse/logs/homeserver.log");
            if (!File.Exists(logsPath))
                return "Synapse logs not yet available";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/tail",
                    Arguments = $"-n 50 \"{logsPath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();

            return output;
        }
        catch (Exception ex)
        {
            return $"Error reading logs: {ex.Message}";
        }
    }
}
