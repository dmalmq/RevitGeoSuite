using System.Threading.Tasks;

namespace RevitGeoSuite.SharedUI.Shell;

public sealed class EchoHandler : IRpcHandler
{
    public string Method => "echo";

    public Task<object?> HandleAsync(object? payload)
    {
        return Task.FromResult<object?>(payload);
    }
}
