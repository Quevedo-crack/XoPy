using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Forms;

namespace XoPy;

internal static class Ipc
{
    public const string MutexName = @"Local\XoPy.SingleInstance.5C1A93";
    public const string PipeName = "XoPy.SingleInstance.5C1A93";

    /// <summary>Respuesta que envía la instancia que SÍ tiene la ventana en pie.</summary>
    public const string Ack = "XOPY-OK";
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
                    using var server = new NamedPipeServerStream(Ipc.PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync().ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
                    using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };

                    // Una sola línea por mensaje: antes se leía hasta el fin del flujo, lo que
                    // obligaba al cliente a cerrar la tubería y hacía imposible contestarle.
                    var json = await reader.ReadLineAsync().ConfigureAwait(false);
                    var request = json is null ? null : StartupRequest.FromJson(json);
                    if (request is null) continue;

                    // La confirmación se envía SOLO si la ventana ha podido atender la orden.
                    // Si esta instancia está colgada, el cliente no recibe nada y abre su
                    // propia ventana: así XoPy nunca se queda «sin abrir nada».
                    var handled = false;
                    try
                    {
                        if (!form.IsDisposed)
                        {
                            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            form.BeginInvoke(new Action(() =>
                            {
                                try { form.HandleExternalRequest(request); }
                                catch { }
                                finally { done.TrySetResult(true); }
                            }));
                            handled = await Task.WhenAny(done.Task, Task.Delay(8000)).ConfigureAwait(false) == done.Task;
                        }
                    }
                    catch { handled = false; }

                    if (handled)
                    {
                        try { await writer.WriteLineAsync(Ipc.Ack).ConfigureAwait(false); } catch { }
                    }
                    try { server.WaitForPipeDrain(); } catch { }
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
    /// <summary>
    /// Entrega la petición a la instancia que ya está abierta y espera su confirmación.
    /// Devuelve false si nadie contesta: en ese caso quien llama DEBE abrir su propia
    /// ventana, porque si no el usuario hace doble clic en XoPy y no aparece nada.
    /// </summary>
    public static bool Send(StartupRequest request)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Ipc.PipeName, PipeDirection.InOut);
            client.Connect(1500);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
            writer.WriteLine(request.ToJson());
            writer.Flush();

            // Espera acotada: una instancia bloqueada no puede dejar al usuario sin ventana.
            var reply = reader.ReadLineAsync();
            if (!reply.Wait(TimeSpan.FromSeconds(10))) return false;
            return string.Equals(reply.Result?.Trim(), Ipc.Ack, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
