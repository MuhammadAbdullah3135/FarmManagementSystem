using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Common;

/// <summary>
/// Where a delta finds out about rows that are gone.
///
/// <para>
/// A delta returns what changed, so a deleted row is a problem: there is nothing left to
/// return, and its absence is indistinguishable from "unchanged" — a device would keep showing
/// a task the server had removed. The tombstone has to come from somewhere, and the audit log
/// is already that somewhere: <c>AuditLogInterceptor</c> writes an entry for every hard delete
/// with the farm, the entity type, the entity's id and the time, and it is indexed on
/// <c>(FarmId, Timestamp)</c>.
/// </para>
///
/// <para>
/// This is why deleting stays a delete. The alternative — making every cacheable entity
/// soft-deletable so the row itself is the tombstone — means a <c>!IsDeleted</c> condition in
/// every query that reads it, where missing one resurrects a deleted record in a list; the
/// audit entry is a single indexed read that works for any entity, including ones added later.
/// Soft-deleted entities (<c>Employee</c> today) need no help here: their own row is the
/// tombstone, carrying <c>IsDeleted</c> and a fresh <c>ModifiedAt</c>.
/// </para>
/// </summary>
public static class ChangeTombstones
{
    /// <summary>Ids of <typeparamref name="TEntity"/> rows deleted on this farm since a cursor.</summary>
    public static async Task<List<Guid>> HardDeletedIdsAsync<TEntity>(
        FmsDbContext context, Guid farmId, DateTime since, CancellationToken cancellationToken = default)
    {
        var entityType = typeof(TEntity).Name;

        var recorded = await context.AuditLogs
            .AsNoTracking()
            .Where(log => log.FarmId == farmId
                          && log.Action == AuditAction.Delete
                          && log.EntityType == entityType
                          && log.Timestamp > since)
            .Select(log => log.EntityId)
            .ToListAsync(cancellationToken);

        // EntityId is a string column, so the parse happens here rather than in SQL. A row
        // whose id cannot be parsed is not a tombstone a client could act on anyway.
        var ids = new List<Guid>(recorded.Count);
        foreach (var id in recorded)
        {
            if (Guid.TryParse(id, out var parsed) && !ids.Contains(parsed))
                ids.Add(parsed);
        }

        return ids;
    }

    /// <summary>
    /// Whether anything of this type was changed in a way a row filter cannot narrow.
    ///
    /// <para>
    /// Used by the computed weight-check status: an <em>added</em> schedule can only create
    /// entries, which a changed-input filter catches, but a modified or deleted one can remove
    /// entries the delta has no id for. Rather than guess, the read asks this and answers
    /// "read the whole collection".
    /// </para>
    /// </summary>
    public static Task<bool> AnyChangedSinceAsync<TEntity>(
        FmsDbContext context, Guid farmId, DateTime since,
        IReadOnlyCollection<AuditAction> actions, CancellationToken cancellationToken = default)
    {
        var entityType = typeof(TEntity).Name;

        return context.AuditLogs
            .AsNoTracking()
            .AnyAsync(log => log.FarmId == farmId
                             && actions.Contains(log.Action)
                             && log.EntityType == entityType
                             && log.Timestamp > since, cancellationToken);
    }
}
