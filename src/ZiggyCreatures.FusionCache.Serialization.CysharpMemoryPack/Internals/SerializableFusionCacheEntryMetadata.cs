using MemoryPack;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion.Serialization.CysharpMemoryPack.Internals;

[MemoryPackable]
internal partial class SerializableFusionCacheEntryMetadata
{
	[MemoryPackIgnore]
	public readonly FusionCacheEntryMetadata? Metadata;

	[MemoryPackInclude]
	public bool IsStale => Metadata?.IsStale ?? default;

	[MemoryPackInclude]
	public long? EagerExpirationTimestamp => Metadata?.EagerExpirationTimestamp;

	[MemoryPackInclude]
	public string? ETag => Metadata?.ETag;

	[MemoryPackInclude]
	public long? LastModifiedTimestamp => Metadata?.LastModifiedTimestamp;

	[MemoryPackInclude]
	public long? Size => Metadata?.Size;

	[MemoryPackInclude]
	public byte? Priority => Metadata?.Priority;

	[MemoryPackInclude]
	public long? SlidingDurationTicks => Metadata?.SlidingDurationTicks;

	[MemoryPackInclude]
	public long? AbsoluteExpirationTimestampTicks => Metadata?.AbsoluteExpirationTimestampTicks;

	[MemoryPackInclude]
	public long? JitterMaxDurationTicks => Metadata?.JitterMaxDurationTicks;

	[MemoryPackConstructor]
	SerializableFusionCacheEntryMetadata(bool isStale, long? eagerExpirationTimestamp, string? etag, long? lastModifiedTimestamp, long? size, byte? priority, long? slidingDurationTicks, long? absoluteExpirationTimestampTicks, long? jitterMaxDurationTicks)
	{
		Metadata = new FusionCacheEntryMetadata(isStale, eagerExpirationTimestamp, etag, lastModifiedTimestamp, size, priority, slidingDurationTicks, absoluteExpirationTimestampTicks, jitterMaxDurationTicks);
	}

	public SerializableFusionCacheEntryMetadata(FusionCacheEntryMetadata? metadata)
	{
		Metadata = metadata;
	}
}

internal class FusionCacheEntryMetadataFormatter : MemoryPackFormatter<FusionCacheEntryMetadata>
{
	public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref FusionCacheEntryMetadata? value)
	{
		if (value is null)
		{
			writer.WriteNullObjectHeader();
			return;
		}

		writer.WritePackable(new SerializableFusionCacheEntryMetadata(value));
	}

	public override void Deserialize(ref MemoryPackReader reader, scoped ref FusionCacheEntryMetadata? value)
	{
		if (reader.PeekIsNull())
		{
			reader.Advance(1);
			value = null;
			return;
		}

		var wrapped = reader.ReadPackable<SerializableFusionCacheEntryMetadata>();
		if (wrapped is null)
		{
			value = null;
			return;
		}

		value = wrapped.Metadata;
	}
}
