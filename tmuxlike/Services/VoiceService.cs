using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using tmuxlike.Models;

namespace tmuxlike.Services;

public enum VoiceState
{
    Disconnected,
    Idle,
    Recording,
    Processing
}

public class VoiceService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly VoiceBridgeConfig _config;
    private ClientWebSocket? _ws;
    private Process? _bridgeProcess;
    private CancellationTokenSource? _cts;
    private VoiceState _state = VoiceState.Disconnected;
    private DateTime _lastToggle = DateTime.MinValue;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "tmuxlike", "voice-service.log");

    private static void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        Debug.WriteLine(line);
        try { File.AppendAllText(LogPath, line + Environment.NewLine); } catch { }
    }

    public event Action<VoiceState>? StateChanged;
    public event Action<string>? PromptReady;
    public event Action<string>? ErrorOccurred;

    public VoiceState State => _state;

    public VoiceService(Dispatcher dispatcher, VoiceBridgeConfig config)
    {
        _dispatcher = dispatcher;
        _config = config;
    }

    private static string? FindScript()
    {
        const string relative = "voice-bridge/voice_bridge.py";

        // 1. Next to the assembly (deployed scenario)
        var asmDir = Path.GetDirectoryName(typeof(VoiceService).Assembly.Location) ?? "";
        var candidate = Path.Combine(asmDir, relative);
        if (File.Exists(candidate)) return Path.GetFullPath(candidate);

        // 2. Walk upward from assembly dir to find repo root (dev scenario)
        var dir = asmDir;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            dir = Path.GetDirectoryName(dir);
        }

        // 3. Walk upward from current working directory
        dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    public void StartBridge()
    {
        // Clear previous log
        try { File.WriteAllText(LogPath, ""); } catch { }

        var scriptPath = FindScript();

        if (scriptPath == null)
        {
            var asmDir = Path.GetDirectoryName(typeof(VoiceService).Assembly.Location) ?? "(null)";
            var cwd = Directory.GetCurrentDirectory();
            Log($"Script not found. Assembly dir: {asmDir}, CWD: {cwd}");
            _dispatcher.Invoke(() => ErrorOccurred?.Invoke("voice_bridge.py not found"));
            return;
        }

        Log($"Script found at: {scriptPath}");

        var configPath = ConfigService.ConfigFilePath;
        Log($"Starting bridge: {_config.PythonPath} \"{scriptPath}\" --config \"{configPath}\"");
        Log($"WorkingDirectory: {Path.GetDirectoryName(scriptPath)}");

        try
        {
            var scriptDir = Path.GetDirectoryName(scriptPath) ?? "";
            _bridgeProcess = Process.Start(new ProcessStartInfo
            {
                FileName = _config.PythonPath,
                Arguments = $"\"{scriptPath}\" --config \"{configPath}\"",
                WorkingDirectory = scriptDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });

            if (_bridgeProcess != null)
            {
                Log($"Process started, PID={_bridgeProcess.Id}");

                _bridgeProcess.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    Log($"[python:stderr] {e.Data}");
                    if (e.Data.Contains("ModuleNotFoundError") || e.Data.Contains("ImportError")
                        || e.Data.Contains("Traceback") || e.Data.Contains("Error"))
                    {
                        _dispatcher.Invoke(() => ErrorOccurred?.Invoke(e.Data));
                    }
                };
                _bridgeProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log($"[python:stdout] {e.Data}");
                };
                _bridgeProcess.BeginErrorReadLine();
                _bridgeProcess.BeginOutputReadLine();

                // Check if process exited immediately (startup failure)
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000);
                    if (_bridgeProcess is { HasExited: true })
                    {
                        Log($"Python process exited early with code {_bridgeProcess.ExitCode}");
                        _dispatcher.Invoke(() => ErrorOccurred?.Invoke(
                            $"Voice bridge exited (code {_bridgeProcess.ExitCode}) — check Python dependencies"));
                    }
                    else if (_bridgeProcess is { HasExited: false })
                    {
                        Log("Python process is running");
                    }
                    else
                    {
                        Log("Python process is null");
                    }
                });
            }
            else
            {
                Log("Process.Start returned null");
            }

            _cts = new CancellationTokenSource();
            _ = ConnectLoop(_cts.Token);
        }
        catch (Exception ex)
        {
            Log($"Failed to start: {ex}");
            SetState(VoiceState.Disconnected);
            _dispatcher.Invoke(() => ErrorOccurred?.Invoke($"Python not found: {ex.Message}"));
        }
    }

    public void StopBridge()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch { }
        }
        _ws?.Dispose();
        _ws = null;

        if (_bridgeProcess is { HasExited: false })
        {
            try { _bridgeProcess.Kill(entireProcessTree: true); } catch { }
        }
        _bridgeProcess?.Dispose();
        _bridgeProcess = null;

        SetState(VoiceState.Disconnected);
    }

    private async Task ConnectLoop(CancellationToken ct)
    {
        // Give the Python process time to start (Whisper model loading can take a few seconds)
        await Task.Delay(4000, ct);

        var attempts = 0;
        while (!ct.IsCancellationRequested)
        {
            attempts++;
            var uri = $"ws://{_config.Host}:{_config.Port}";
            Log($"Connection attempt {attempts} to {uri}");

            // Check if process is still alive before connecting
            if (_bridgeProcess is { HasExited: true })
            {
                Log($"Python process already exited (code {_bridgeProcess.ExitCode}), aborting connect");
                _dispatcher.Invoke(() => ErrorOccurred?.Invoke(
                    $"Voice bridge crashed (code {_bridgeProcess.ExitCode})"));
                break;
            }

            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(_config.ConnectTimeoutMs);

                await _ws.ConnectAsync(new Uri(uri), connectCts.Token);
                Log("Connected!");
                attempts = 0;
                SetState(VoiceState.Idle);
                await ReceiveLoop(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Connect failed: {ex.Message}");
                SetState(VoiceState.Disconnected);

                if (attempts == 3)
                {
                    _dispatcher.Invoke(() => ErrorOccurred?.Invoke(
                        "Cannot connect to voice bridge — check if Python started correctly"));
                }
            }

            if (!ct.IsCancellationRequested)
                await Task.Delay(_config.ReconnectDelayMs, ct).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync(buffer, ct);
            }
            catch
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Log($"Received: {json}");
            HandleEvent(json);
        }

        SetState(VoiceState.Disconnected);
    }

    private void HandleEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var evt = root.GetProperty("event").GetString();

            switch (evt)
            {
                case "LISTENING":
                    SetState(VoiceState.Recording);
                    break;
                case "PROCESSING":
                    SetState(VoiceState.Processing);
                    break;
                case "FINAL_PROMPT":
                    var text = root.GetProperty("text").GetString() ?? "";
                    SetState(VoiceState.Idle);
                    _dispatcher.Invoke(() => PromptReady?.Invoke(text));
                    break;
                case "ERROR":
                    var msg = root.GetProperty("message").GetString() ?? "Unknown error";
                    SetState(VoiceState.Idle);
                    _dispatcher.Invoke(() => ErrorOccurred?.Invoke(msg));
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Bad event: {ex.Message}");
        }
    }

    public void Toggle()
    {
        // Debounce: ignore repeated calls within 300ms (keyboard repeat floods)
        var now = DateTime.UtcNow;
        if ((now - _lastToggle).TotalMilliseconds < 300)
            return;
        _lastToggle = now;

        Log($"Toggle called, state={_state}");
        switch (_state)
        {
            case VoiceState.Idle:
                _ = SendAction("PTT_DOWN");
                break;
            case VoiceState.Recording:
                _ = SendAction("PTT_UP");
                break;
            case VoiceState.Processing:
                _ = SendAction("CANCEL");
                break;
        }
    }

    private async Task SendAction(string action)
    {
        if (_ws is not { State: WebSocketState.Open })
        {
            Log($"Cannot send {action}: WebSocket not open");
            return;
        }
        var json = JsonSerializer.Serialize(new { action });
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            Log($"Sent: {action}");
        }
        catch (Exception ex)
        {
            Log($"Send failed: {ex.Message}");
        }
    }

    private void SetState(VoiceState state)
    {
        if (_state == state) return;
        Log($"State: {_state} -> {state}");
        _state = state;
        _dispatcher.Invoke(() => StateChanged?.Invoke(state));
    }

    public void Dispose()
    {
        StopBridge();
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
