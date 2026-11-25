using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace FoxuMarsDisplay.Core.Host;

public sealed record IpcRequest(
    string Command,
    string? SerialNumber = null,
    string? ThemePath = null,
    string? SchemeJsonPath = null,
    int? Value = null,
    bool? Flag = null);

public sealed record IpcResponse(
    bool Ok,
    string? Message = null,
    object? Data = null);

/// <summary>
/// Lightweight Named Pipe server allowing external tools (e.g. SnakeMarsTheme Studio)
/// to query active screens and apply themes live without restarting FoxuMarsDisplay.
/// Grants ReadWrite to AuthenticatedUsers so non-elevated Studio clients can connect
/// to an elevated FoxuMarsDisplay host.
/// </summary>
public sealed class IpcPipeServer : IDisposable
{
    public const string PipeName = "FoxuMarsDisplay_IPC_v1";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly Func<IpcRequest, IpcResponse> _handler;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public IpcPipeServer(Func<IpcRequest, IpcResponse> handler)
    {
        _handler = handler;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
    }

    private static NamedPipeServerStream CreateAclPipeServer()
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateAclPipeServer();
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using (server)
                {
                    using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                    using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
                    {
                        AutoFlush = true
                    };

                    string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    IpcResponse response;
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        response = new IpcResponse(false, "Empty request");
                    }
                    else
                    {
                        var req = JsonSerializer.Deserialize<IpcRequest>(line, JsonOpts);
                        response = req is null
                            ? new IpcResponse(false, "Invalid JSON request")
                            : _handler(req);
                    }

                    string json = JsonSerializer.Serialize(response, JsonOpts);
                    await writer.WriteLineAsync(json.AsMemory(), ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch
            {
                server?.Dispose();
                try { await Task.Delay(100, ct).ConfigureAwait(false); } catch { break; }
            }
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listenTask?.Wait(500); } catch { }
        _cts?.Dispose();
        _cts = null;
    }
}
