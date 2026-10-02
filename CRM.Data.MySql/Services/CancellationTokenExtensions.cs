namespace CRM.Data.Services;

internal static class CancellationTokenExtensions
{
    public static async Task<bool> WaitForDelayAsync(
        this CancellationToken cancellationToken,
        TimeSpan delay)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var cancellationSignal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellationSignal);

        var delayTask = Task.Delay(delay);
        return await Task.WhenAny(delayTask, cancellationSignal.Task) == delayTask;
    }
}
