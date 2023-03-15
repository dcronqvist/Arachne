using System;
using System.Threading;
using System.Threading.Tasks;

namespace Arachne;

public class DeliveryService<T>
{
    private T? _deliveredObject;
    private bool _isDeliveryInProgress;
    private TaskCompletionSource<T> _deliveryCompletionSource = new TaskCompletionSource<T>();

    public async Task<T?> AwaitDeliveryAsync(int timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        var cancellationToken = cts.Token;

        using var registration = cancellationToken.Register(() =>
        {
            _deliveryCompletionSource.TrySetCanceled(cancellationToken);
        });

        try
        {
            _isDeliveryInProgress = true;
            var deliveredObject = await _deliveryCompletionSource.Task;
            _isDeliveryInProgress = false;
            return deliveredObject;
        }
        catch (TaskCanceledException)
        {
            return _deliveredObject;
        }
        finally
        {
            _isDeliveryInProgress = false;
            this._deliveryCompletionSource = new TaskCompletionSource<T>();
        }
    }

    public bool TryDeliverToWaiter(T obj)
    {
        if (!_isDeliveryInProgress)
        {
            return false;
        }

        if (_deliveryCompletionSource.TrySetResult(obj))
        {
            _deliveredObject = obj;
            return true;
        }

        return false;
    }
}