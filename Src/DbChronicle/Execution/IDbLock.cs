using System;

namespace DbChronicle.Execution
{
    public interface IDbLock : IDisposable
    {
        bool TryAcquire(TimeSpan timeout);
    }
}