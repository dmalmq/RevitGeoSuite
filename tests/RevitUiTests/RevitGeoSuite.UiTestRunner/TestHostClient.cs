using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>Talks to the RevitGeoSuite.TestHost add-in inside Revit over its named pipe, one JSON line each way.</summary>
internal sealed class TestHostClient : IDisposable
{
    public const string PipeName = "RevitGeoSuite.TestHost";

    private readonly NamedPipeClientStream pipe;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;

    private TestHostClient(NamedPipeClientStream pipe)
    {
        this.pipe = pipe;
        var utf8 = new UTF8Encoding(false);
        reader = new StreamReader(pipe, utf8, false, 1 << 16, leaveOpen: true);
        writer = new StreamWriter(pipe, utf8, 1 << 16, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    /// <summary>Connects to the test host, waiting for Revit to finish starting.</summary>
    public static TestHostClient Connect(TimeSpan timeout)
    {
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        try
        {
            pipe.Connect((int)timeout.TotalMilliseconds);
        }
        catch (TimeoutException)
        {
            pipe.Dispose();
            throw new InvalidOperationException(
                $"No Revit with the test host answered within {timeout.TotalSeconds:0} s. Install it once (Install-TestHost.ps1), close Revit, and start the run with Run-UiTests.ps1 -StartRevit: the test host stays inert in a Revit started any other way. Or close the other runner that holds the pipe.");
        }

        return new TestHostClient(pipe);
    }

    public JsonObject Call(string command, object? arguments = null)
    {
        JsonObject request = arguments == null ? new JsonObject() : JsonSerializer.SerializeToNode(arguments)!.AsObject();
        request["cmd"] = command;
        writer.WriteLine(request.ToJsonString());
        string line = reader.ReadLine() ?? throw new IOException("Revit closed the test host pipe.");
        JsonObject reply = JsonNode.Parse(line)!.AsObject();
        if (reply["ok"]?.GetValue<bool>() != true)
        {
            throw new TestHostException(command, reply["error"]?.GetValue<string>() ?? line);
        }

        return reply;
    }

    public int LastEvent() => Call("events", new { after = int.MaxValue })["last"]!.GetValue<int>();

    public List<RevitEvent> EventsAfter(int after) =>
        Call("events", new { after })["events"]!.AsArray().Select(node => RevitEvent.From(node!.AsObject())).ToList();

    /// <summary>Waits until Revit reports an event that matches, after sequence <paramref name="after"/>.</summary>
    public RevitEvent WaitForEvent(int after, Func<RevitEvent, bool> match, TimeSpan timeout, string what)
    {
        DateTime end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            RevitEvent? found = EventsAfter(after).FirstOrDefault(match);
            if (found != null)
            {
                return found;
            }

            Thread.Sleep(250);
        }

        string seen = string.Join("; ", EventsAfter(after).Select(item => item.ToString()));
        throw new TimeoutException($"Revit did not report {what} within {timeout.TotalSeconds:0} s. Seen: {(seen.Length == 0 ? "nothing" : seen)}");
    }

    public void Dispose()
    {
        reader.Dispose();
        writer.Dispose();
        pipe.Dispose();
    }
}

internal sealed class TestHostException(string command, string error) : Exception($"Test host '{command}' failed: {error}");

/// <summary>One thing Revit did: a transaction committed, undone or redone, or a dialog it showed.</summary>
internal sealed record RevitEvent(int Seq, string Kind, IReadOnlyList<string> Names, string Document, string DialogId, string Message)
{
    public static RevitEvent From(JsonObject node) => new(
        node["seq"]!.GetValue<int>(),
        node["kind"]?.GetValue<string>() ?? string.Empty,
        node["names"]?.AsArray().Select(name => name!.GetValue<string>()).ToList() ?? new List<string>(),
        node["document"]?.GetValue<string>() ?? string.Empty,
        node["dialogId"]?.GetValue<string>() ?? string.Empty,
        node["message"]?.GetValue<string>() ?? string.Empty);

    public bool Is(string kind, string name) => Kind == kind && Names.Contains(name, StringComparer.Ordinal);

    public override string ToString() => Kind == "dialog"
        ? $"#{Seq} dialog {DialogId}: {Message}"
        : $"#{Seq} {Kind} [{string.Join(", ", Names)}] in {Document}";
}
