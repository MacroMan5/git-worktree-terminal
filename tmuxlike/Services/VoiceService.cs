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

    public event Action<VoiceState>? StateChanged;
    public event Action<string>? PromptReady;
    public event Action<string>? ErrorOccurred;

    public VoiceState State => _state;

    public VoiceService(Dispatcher dispatcher, VoiceBridgeConfig config)
    {
        _dispatcher = dispatcher;
        _config = config;
    }

    public void StartBridge()
    {
        var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voice-bridge", "voice_bridge.py");
        if (!File.Exists(scriptPath))
        {
            scriptPath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "voice-bridge", "voice_bridge.py"));
        }

        try
        {
            _bridgeProcess = Process.Start(new ProcessStartInfo
            {
                FileName = _config.PythonPath,
                Arguments = $"\"{scriptPath}\" --config \"{ConfigService.ConfigFilePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });

            if (_bridgeProcess != null)
            {
                _bridgeProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.WriteLine($"[voice-bridge] {e.Data}");
                };
                _bridgeProcess.BeginErrorReadLine();
            }

            _cts = new CancellationTokenSource();
            _ = ConnectLoop(_cts.Token);
        }
        catch (Exception ex)
        {
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
        // Give the Python process time to start its WebSocket server
        await Task.Delay(2000, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(_config.ConnectTimeoutMs);

                await _ws.ConnectAsync(new Uri($"ws://{_config.Host}:{_config.Port}"), connectCts.Token);
                SetState(VoiceState.Idle);
                await ReceiveLoop(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                SetState(VoiceState.Disconnected);
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
            Debug.WriteLine($"[VoiceService] Bad event: {ex.Message}");
        }
    }

    public void Toggle()
    {
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
        if (_ws is not { State: WebSocketState.Open }) return;
        var json = JsonSerializer.Serialize(new { action });
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceService] Send failed: {ex.Message}");
        }
    }

    private void SetState(VoiceState state)
    {
        if (_state == state) return;
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
