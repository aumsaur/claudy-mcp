using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PetOverlay;

public class PipeServer
{
    private readonly string _pipeName;
    private readonly Func<PromptRequest, Task<PromptResponse>> _handler;
    private readonly CancellationTokenSource _cts = new();

    public PipeServer(string pipeName, Func<PromptRequest, Task<PromptResponse>> handler)
    {
        _pipeName = pipeName;
        _handler = handler;
    }

    public void Start() => _ = Task.Run(() => AcceptLoopAsync(_cts.Token));

    public void Stop() => _cts.Cancel();

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(token);
                await HandleConnectionAsync(pipe, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // keep serving even if a single connection misbehaves
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

        var line = await reader.ReadLineAsync(token);
        if (string.IsNullOrEmpty(line)) return;

        PromptRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<PromptRequest>(line);
        }
        catch
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new PromptResponse { Status = "error", Answer = "invalid request" }));
            return;
        }

        if (request is null) return;

        PromptResponse response;
        try
        {
            response = await _handler(request);
        }
        catch (Exception ex)
        {
            response = new PromptResponse { Status = "error", Answer = ex.Message };
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
    }
}
