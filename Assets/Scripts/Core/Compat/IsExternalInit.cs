// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Compiler-recognised marker that enables <c>init</c>-only setters and positional records.
    /// </summary>
    /// <remarks>
    /// WHY this exists: ADR-0003 pins the project to C# 9 on Unity 6 LTS, whose reference assemblies
    /// target netstandard2.1 and therefore do not ship <c>IsExternalInit</c>. Without this polyfill the
    /// compiler rejects every <c>init</c> accessor with CS0518. The type is deliberately
    /// <c>internal</c> so each assembly that needs it carries its own copy without colliding at link
    /// time, and it is compiled out on runtimes that already define it.
    /// </remarks>
#if !NET5_0_OR_GREATER
    internal static class IsExternalInit
    {
    }
#endif
}
