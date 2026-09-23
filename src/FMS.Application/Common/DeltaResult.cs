namespace FMS.Application.Common;

/// <summary>
/// A page of rows that can also answer "what changed since you last asked".
///
/// <para>
/// It extends <see cref="PagedResult{T}"/> rather than replacing it so a client that only wants
/// rows reads exactly what it always did — <c>items</c>, <c>page</c>, <c>totalCount</c> — and a
/// client that holds a cached copy additionally gets the three fields it needs to stay in step:
/// </para>
///
/// <list type="bullet">
/// <item>
/// <see cref="DeletedIds"/> — rows that existed when the caller last synced and do not now.
/// A delta cannot express a deletion by omission (the client cannot tell "deleted" from "not
/// changed"), so deletions are stated; without this a device would keep showing a task or an
/// employee that the server had removed.
/// </item>
/// <item>
/// <see cref="Cursor"/> — the server's own timestamp for this read. The client stores it and
/// sends it back as <c>updatedSince</c> next time, so a device whose clock is wrong by hours
/// cannot skip rows.
/// </item>
/// <item>
/// <see cref="RequiresFullSync"/> — the honest answer when the cursor cannot be answered
/// precisely (too old, unparseable, or a change the delta cannot narrow). The caller should
/// then read the collection without <c>updatedSince</c> and replace its copy.
/// </item>
/// </list>
///
/// <para>
/// Only the collections that devices cache use this type. The other paged endpoints keep
/// returning <see cref="PagedResult{T}"/>, so no response grows fields it never fills.
/// </para>
/// </summary>
public class DeltaResult<T> : PagedResult<T>
{
    /// <summary>Ids of rows that were removed server-side since the caller's cursor.</summary>
    public List<Guid> DeletedIds { get; set; } = new();

    /// <summary>The server's timestamp for this read — send it back as <c>updatedSince</c>.</summary>
    public DateTime Cursor { get; set; } = DateTime.UtcNow;

    /// <summary>True when the caller must read the whole collection instead of a delta.</summary>
    public bool RequiresFullSync { get; set; }

    public static DeltaResult<T> From(PagedResult<T> page, DateTime cursor) => new()
    {
        Items = page.Items,
        Page = page.Page,
        PageSize = page.PageSize,
        TotalCount = page.TotalCount,
        Cursor = cursor
    };

    /// <summary>A read that cannot be a delta: the caller is told to replace its copy.</summary>
    public static DeltaResult<T> FullSyncRequired(DateTime cursor) => new()
    {
        Cursor = cursor,
        RequiresFullSync = true
    };
}
