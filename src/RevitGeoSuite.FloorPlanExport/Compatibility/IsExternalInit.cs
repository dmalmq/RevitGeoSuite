// .NET Framework lacks this type, which init accessors need; .NET has it.
#if NETFRAMEWORK
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
#endif
