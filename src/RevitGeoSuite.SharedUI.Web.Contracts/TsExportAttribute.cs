using System;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class TsExportAttribute : Attribute
{
    public string? Name { get; set; }
}
