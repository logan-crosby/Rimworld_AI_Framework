// Polyfill for C# 9 init-only setters on .NET Framework 4.7.2
// IsExternalInit was introduced in .NET 5; net472 needs this shim.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
