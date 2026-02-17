using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

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
    private const string Host = "localhost";
    private const int Port = 5005;
    private const int ReconnectDelayMs = 5000;
    private const int ConnectTimeoutMs = 3000;

    private readonly Dispatcher _dispatcher;
    private ClientWebSocket? _ws;
    private Process? _bridgeProcess;
    private CancellationTokenSource? _cts;
    private VoiceState _state = VoiceState.Disconnected;

    public event Action<VoiceState>? StateChanged;
    public event Action<string>? PromptReady;
    public event Action<string>? ErrorOccurred;

    public VoiceState State => _state;

    public VoiceService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
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
                FileName = "python",
                Arguments = $"\"{scriptPath}\"",
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
