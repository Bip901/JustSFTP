using System;
using System.Threading;

namespace JustSFTP.Client;

/// <summary>
/// Like <see cref="IAsyncDisposable"/>, but allows cancelling the DisposeAsync call.
/// </summary>
public interface IAsyncDisposableCancelable : IAsyncDisposable
{
    /// <summary>
    /// Sets a cancellation token that would be used by <see cref="IAsyncDisposable.DisposeAsync"/>.
    /// This overrides any previous tokens passed to this method.
    /// </summary>
    void SetDisposeCancellationToken(CancellationToken cancellationToken);
}
