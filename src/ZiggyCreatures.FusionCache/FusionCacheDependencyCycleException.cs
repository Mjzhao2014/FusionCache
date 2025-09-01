namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// The exception that is thrown when a circular dependency is detected during cache dependency operations.
/// </summary>
[Serializable]
public class FusionCacheDependencyCycleException
	: Exception
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FusionCacheDependencyCycleException"/> class.
	/// </summary>
	public FusionCacheDependencyCycleException()
	{
	}

	/// <summary>Initializes a new instance of the <see cref="FusionCacheDependencyCycleException"/> class with a specified error message.</summary>
	/// <param name="message">The message that describes the error.</param>
	public FusionCacheDependencyCycleException(string? message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="FusionCacheDependencyCycleException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
	/// <param name="message">The error message that explains the reason for the exception.</param>
	/// <param name="innerException">The exception that is the cause of the current exception. If the innerException parameter is not a null reference (Nothing in Visual Basic), the current exception is raised in a catch block that handles the inner exception.</param>
	public FusionCacheDependencyCycleException(string? message, Exception? innerException)
		: base(message, innerException)
	{
	}

	/// <summary>
	/// Gets or sets the key that was involved in the circular dependency detection.
	/// </summary>
	public string? Key { get; set; }

	/// <summary>
	/// Gets or sets the dependency chain that led to the circular dependency.
	/// </summary>
	public IReadOnlyList<string>? DependencyChain { get; set; }
}
