namespace RevitGeoSuite.Core.Modules;

public sealed class RevitCommandDescriptor
{
    public string CommandId { get; set; } = string.Empty;

    public string ButtonText { get; set; } = string.Empty;

    public string ToolTip { get; set; } = string.Empty;

    public string CommandClassName { get; set; } = string.Empty;

    public string AssemblyPath { get; set; } = string.Empty;
}
