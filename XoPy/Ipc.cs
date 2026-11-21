using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Forms;

namespace XoPy;

internal static class Ipc
{
    public const string MutexName = @"Local\XoPy.SingleInstance.5C1A93";
    public const string PipeName = "XoPy.SingleInstance.5C1A93";
}

internal static class IpcServer
{
    private static bool _started;

    public static void Start(MainForm form)
    {
        if (_started) return;
        _started = true;

        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Ipc.PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync().ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var request = StartupRequest.FromJson(json);
                    if (request is not null)
                    {
                        try
                        {
                            if (form.IsDisposed) return;
                            form.BeginInvoke(new Action(() => form.HandleExternalRequest(request)));
                        }
                        catch { }
                    }
                }
                catch
                {
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        });
    }
}

internal static class IpcClient
{
    public static bool Send(StartupRequest request)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Ipc.PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.Write(request.ToJson());
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
