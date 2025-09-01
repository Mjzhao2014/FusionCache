#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
	/// <summary>
	/// Dummy type required to support init-only properties when targeting older frameworks.
	/// </summary>
	internal static class IsExternalInit
	{
	}
}
#endif
