using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

// Runs the client against a stand-in for the add-in's pipe, speaking the same one-line JSON protocol.
public class TestHostClientTests
{
    [Fact]
    public async Task Sends_the_command_with_its_arguments_and_returns_the_reply()
    {
        JsonObject? received = null;
        Task server = Serve(request =>
        {
            received = request;
            return """{"ok":true,"posted":"ID_EDIT_UNDO","last":7}""";
        });

        using (TestHostClient client = TestHostClient.Connect(TimeSpan.FromSeconds(10)))
        {
            JsonObject reply = client.Call("post", new { id = "ID_EDIT_UNDO" });
            Assert.Equal(7, reply["last"]!.GetValue<int>());
        }

        await server;
        Assert.Equal("post", received!["cmd"]!.GetValue<string>());
        Assert.Equal("ID_EDIT_UNDO", received["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Throws_with_the_hosts_error()
    {
        Task server = Serve(_ => """{"ok":false,"error":"InvalidOperationException: outside the test folder"}""");

        using (TestHostClient client = TestHostClient.Connect(TimeSpan.FromSeconds(10)))
        {
            TestHostException error = Assert.Throws<TestHostException>(() => client.Call("open", new { path = "x" }));
            Assert.Contains("outside the test folder", error.Message);
        }

        await server;
    }

    [Fact]
    public async Task Waits_for_a_matching_event()
    {
        int calls = 0;
        Task server = Serve(_ => ++calls < 3
            ? """{"ok":true,"events":[],"last":4}"""
            : """{"ok":true,"events":[{"seq":5,"kind":"undo","names":["Kanizu GIS: Set up model"],"document":"LUMINE"}],"last":5}""",
            requests: 3);

        using (TestHostClient client = TestHostClient.Connect(TimeSpan.FromSeconds(10)))
        {
            RevitEvent found = client.WaitForEvent(4, item => item.Is("undo", "Kanizu GIS: Set up model"), TimeSpan.FromSeconds(10), "the undo");
            Assert.Equal(5, found.Seq);
        }

        await server;
    }

    private static Task Serve(Func<JsonObject, string> reply, int requests = 1)
    {
        var pipe = new NamedPipeServerStream(TestHostClient.PipeName, PipeDirection.InOut, 1);
        return Task.Run(() =>
        {
            using (pipe)
            {
                pipe.WaitForConnection();
                var utf8 = new UTF8Encoding(false);
                using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
                for (int index = 0; index < requests; index++)
                {
                    writer.WriteLine(reply(JsonNode.Parse(reader.ReadLine()!)!.AsObject()));
                }
            }
        });
    }
}
