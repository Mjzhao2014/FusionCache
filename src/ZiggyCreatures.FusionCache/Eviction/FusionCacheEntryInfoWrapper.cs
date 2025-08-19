using ZiggyCreatures.Caching.Fusion.Internals;
using ZiggyCreatures.Caching.Fusion.Internals.Memory;

namespace ZiggyCreatures.Caching.Fusion.Eviction;

/// <summary>
/// Wrapper that adapts an internal IFusionCacheMemoryEntry to the public IFusionCacheEntryInfo interface.
/// </summary>
internal sealed class FusionCacheEntryInfoWrapper : IFusionCacheEntryInfo
{
	private readonly IFusionCacheMemoryEntry _entry;

	public FusionCacheEntryInfoWrapper(IFusionCacheMemoryEntry entry)
	{
		_entry = entry ?? throw new ArgumentNullException(nameof(entry));
	}

	public long Timestamp => _entry.Timestamp;

	public long LogicalExpirationTimestamp => _entry.LogicalExpirationTimestamp;

	public string[]? Tags => _entry.Tags;

	public bool IsLogicallyExpired() => FusionCacheInternalUtils.IsLogicallyExpired(_entry);

	public long? GetSize() => _entry.Metadata?.Size;

	public byte? GetPriority() => _entry.Metadata?.Priority;
}