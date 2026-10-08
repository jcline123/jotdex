using Jotdex.Core.Integrations;

namespace Jotdex.Infrastructure.Integrations;

public sealed class VaultWriteCoordinator : IVaultWriteCoordinator, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await action(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public T Execute<T>(Func<T> action)
    {
        _gate.Wait();
        try
        {
            return action();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
