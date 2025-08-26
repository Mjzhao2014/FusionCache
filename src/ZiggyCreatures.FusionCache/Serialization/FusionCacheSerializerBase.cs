namespace ZiggyCreatures.Caching.Fusion.Serialization;

/// <summary>
/// Abstract base class for FusionCache serializers that provides common implementation patterns
/// and standardizes async/sync method relationships.
/// </summary>
public abstract class FusionCacheSerializerBase : IFusionCacheSerializer
{
	/// <summary>
	/// Serializes the specified object into a byte array.
	/// This is the core method that derived classes must implement.
	/// </summary>
	/// <typeparam name="T">The type of the object parameter.</typeparam>
	/// <param name="obj">The object to serialize.</param>
	/// <returns>The byte array which represents the serialized object.</returns>
	public abstract byte[] Serialize<T>(T? obj);

	/// <summary>
	/// Deserializes the specified byte array data into an object of type T.
	/// This is the core method that derived classes must implement.
	/// </summary>
	/// <typeparam name="T">The type of the object to be returned.</typeparam>
	/// <param name="data">The data to deserialize.</param>
	/// <returns>The deserialized object.</returns>
	public abstract T? Deserialize<T>(byte[] data);

	/// <summary>
	/// Serializes the specified object into a byte array asynchronously.
	/// The default implementation delegates to the synchronous Serialize method.
	/// Derived classes can override this for true async serialization.
	/// </summary>
	/// <typeparam name="T">The type of the object parameter.</typeparam>
	/// <param name="obj">The object to serialize.</param>
	/// <param name="token">The cancellation token.</param>
	/// <returns>The byte array which represents the serialized object.</returns>
	public virtual ValueTask<byte[]> SerializeAsync<T>(T? obj, CancellationToken token = default)
	{
		return new ValueTask<byte[]>(Serialize(obj));
	}

	/// <summary>
	/// Deserializes the specified byte array data into an object of type T asynchronously.
	/// The default implementation delegates to the synchronous Deserialize method.
	/// Derived classes can override this for true async deserialization.
	/// </summary>
	/// <typeparam name="T">The type of the object to be returned.</typeparam>
	/// <param name="data">The data to deserialize.</param>
	/// <param name="token">The cancellation token.</param>
	/// <returns>The deserialized object.</returns>
	public virtual ValueTask<T?> DeserializeAsync<T>(byte[] data, CancellationToken token = default)
	{
		return new ValueTask<T?>(Deserialize<T>(data));
	}

	/// <summary>
	/// Returns a string representation of the serializer.
	/// </summary>
	/// <returns>The name of the serializer type.</returns>
	public override string ToString() => GetType().Name;
}