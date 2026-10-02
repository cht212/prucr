using CRM.Data.Services;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CRM.Data.Data;

public sealed class CrmRealtimeSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly CrmRealtimeNotifier _notifier;

    public CrmRealtimeSaveChangesInterceptor(CrmRealtimeNotifier notifier)
    {
        _notifier = notifier;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (result > 0) _notifier.PublishChange();
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (result > 0) _notifier.PublishChange();
        return ValueTask.FromResult(result);
    }
}
