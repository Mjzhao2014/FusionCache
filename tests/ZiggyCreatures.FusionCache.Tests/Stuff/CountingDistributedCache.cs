using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace FusionCacheTests.Stuff
{
    internal class CountingDistributedCache : IDistributedCache
    {
        private readonly IDistributedCache _inner;
        public int GetCalls => _getCalls;
        private int _getCalls;
        public int SetCalls => _setCalls;
        private int _setCalls;
        public int RemoveCalls => _removeCalls;
        private int _removeCalls;

        public CountingDistributedCache(IDistributedCache inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public byte[]? Get(string key)
        {
            Interlocked.Increment(ref _getCalls);
            return _inner.Get(key);
        }

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            Interlocked.Increment(ref _getCalls);
            return await _inner.GetAsync(key, token).ConfigureAwait(false);
        }

        public void Refresh(string key)
        {
            _inner.Refresh(key);
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            return _inner.RefreshAsync(key, token);
        }

        public void Remove(string key)
        {
            Interlocked.Increment(ref _removeCalls);
            _inner.Remove(key);
        }

        public async Task RemoveAsync(string key, CancellationToken token = default)
        {
            Interlocked.Increment(ref _removeCalls);
            await _inner.RemoveAsync(key, token).ConfigureAwait(false);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Interlocked.Increment(ref _setCalls);
            _inner.Set(key, value, options);
        }

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Interlocked.Increment(ref _setCalls);
            await _inner.SetAsync(key, value, options, token).ConfigureAwait(false);
        }
    }
}
