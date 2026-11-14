using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// One client at a time on a named pipe that only the signed-in user can open. Each request is one
/// line of JSON, <c>{"cmd": "...", ...}</c>; each reply is one line, <c>{"ok": true, ...}</c> or
/// <c>{"ok": false, "error": "..."}</c>.
/// </summary>
internal sealed class PipeServer
{
    public const string PipeName = "RevitGeoSuite.TestHost";

    private readonly Func<Dictionary<string, object?>, Dictionary<string, object?>> handle;
    private readonly Action onDisconnect;
    private readonly JavaScriptSerializer json = new() { MaxJsonLength = int.MaxValue };
    private volatile bool stopping;
    private Thread? thread;
    private NamedPipeServerStream? current;

    public PipeServer(Func<Dictionary<string, object?>, Dictionary<string, object?>> handle, Action onDisconnect)
    {
        this.handle = handle;
        this.onDisconnect = onDisconnect;
    }

    public void Start()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "RevitGeoSuite.TestHost pipe" };
        thread.Start();
    }

    public void Stop()
    {
        stopping = true;
        try { current?.Dispose(); } catch { }
    }

    private void Run()
    {
        while (!stopping)
        {
            try
            {
                using NamedPipeServerStream pipe = Create();
                current = pipe;
                pipe.WaitForConnection();
                Serve(pipe);
            }
            catch (Exception ex) when (!stopping)
            {
                Trace.WriteLine($"RevitGeoSuite.TestHost pipe: {ex}");
                Thread.Sleep(500);
            }
            catch
            {
                return;
            }
            finally
            {
                onDisconnect();
            }
        }
    }

    private static NamedPipeServerStream Create()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 1 << 16, 1 << 16, security);
    }

    private void Serve(NamedPipeServerStream pipe)
    {
        var utf8 = new UTF8Encoding(false);
        using var reader = new StreamReader(pipe, utf8, false, 1 << 16, leaveOpen: true);
        using var writer = new StreamWriter(pipe, utf8, 1 << 16, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        string? line;
        while (!stopping && (line = reader.ReadLine()) != null)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            Dictionary<string, object?> reply;
            try
            {
                var request = json.Deserialize<Dictionary<string, object?>>(line) ?? new Dictionary<string, object?>();
                reply = handle(request);
            }
            catch (Exception ex)
            {
                reply = Commands.Fail(ex);
            }

            writer.WriteLine(json.Serialize(reply));
        }
    }
}
