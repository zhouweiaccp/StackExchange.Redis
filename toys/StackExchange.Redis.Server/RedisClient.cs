using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.Server
{
    public sealed class RedisClient : IDisposable
    {
        internal int SkipReplies { get; set; }
        internal bool ShouldSkipResponse()
        {
            if (SkipReplies > 0)
            {
                SkipReplies--;
                return true;
            }
            return false;
        }
        private HashSet<RedisChannel> _subscripions;
        public int SubscriptionCount => _subscripions?.Count ?? 0;
        internal int Subscribe(RedisChannel channel)
        {
            var subs = _subscripions;
            if(subs == null)
            {
                subs = new HashSet<RedisChannel>(); // but need to watch for compete
                subs = Interlocked.CompareExchange(ref _subscripions, subs, null) ?? subs;
            }
            lock (subs)
            {
                subs.Add(channel);
                return subs.Count;
            }
        }
        internal int Unsubscribe(RedisChannel channel)
        {
            var subs = _subscripions;
            if (subs == null) return 0;
            lock (subs)
            {
                subs.Remove(channel);
                return subs.Count;
            }
        }

        internal bool IsSubscribed(RedisChannel channel)
        {
            var subs = _subscripions;
            if (subs == null) return false;
            lock (subs)
            {
                return subs.Contains(channel);
            }
        }

        public int Database { get; set; }
        public string Name { get; set; }
        internal IDuplexPipe LinkedPipe { get; set; }
        public bool Closed { get; internal set; }
        public int Id { get; internal set; }

        public void Dispose()
        {
            Closed = true;
            var pipe = LinkedPipe;
            LinkedPipe = null;
            if (pipe != null)
            {
                try { pipe.Input.CancelPendingRead(); } catch { }
                try { pipe.Input.Complete(); } catch { }
                try { pipe.Output.CancelPendingFlush(); } catch { }
                try { pipe.Output.Complete(); } catch { }
                if (pipe is IDisposable d) try { d.Dispose(); } catch { }
            }
        }

        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1);
        internal Task TakeWriteLockAsync() => _writeLock.WaitAsync();
        internal void ReleasseWriteLock() => _writeLock.Release();
    }
}
