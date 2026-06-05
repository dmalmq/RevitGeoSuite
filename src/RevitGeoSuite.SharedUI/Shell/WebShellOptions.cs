using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RevitGeoSuite.SharedUI.Shell;

public sealed class WebShellOptions
{
    public string InitialRoute { get; init; } = string.Empty;
    public string TitleKey { get; init; } = string.Empty;
    public IntPtr OwnerHandle { get; init; }
    public IReadOnlyList<IRpcHandler> Handlers { get; init; } = Array.Empty<IRpcHandler>();
    
    public Action<WebRpcBridge, JobManager>? RegisterHandlers { get; init; }
}

public interface IRpcHandler
{
    string Method { get; }
    Task<object?> HandleAsync(object? payload);
}
