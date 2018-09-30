using System;
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

        [Flags]
        private enum ClientFlags
        {
            None = 0,
            Closed = 1 << 0,
            HasHadSubscsription = 1 << 1
        }
        private ClientFlags _flags;
        private bool HasFlag(ClientFlags flag) => (_flags & flag) != 0;
        private void SetFlag(ClientFlags flag, bool value)
        {
            if (value) _flags |= flag;
            else _flags &= ~flag;
        }

        private int _subscriptionCount;
        public bool HasHadSubscsription => HasFlag(ClientFlags.HasHadSubscsription);
        public int SubscriptionCount => Thread.VolatileRead(ref _subscriptionCount);

        internal int IncrSubscsriptionCount()
        {
            SetFlag(ClientFlags.HasHadSubscsription, true);
            return Interlocked.Increment(ref _subscriptionCount);
        }

        internal int DecrSubscsriptionCount() => Interlocked.Decrement(ref _subscriptionCount);

        public int Database { get; set; }
        public string Name { get; set; }
        internal IDuplexPipe LinkedPipe { get; set; }
        public bool Closed => HasFlag(ClientFlags.Closed);
        public int Id { get; internal set; }

        internal void SetClosed() => SetFlag(ClientFlags.Closed, true);

        public void Dispose()
        {
            SetClosed();
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
        internal void ReleaseWriteLock() => _writeLock.Release();
    }
}
