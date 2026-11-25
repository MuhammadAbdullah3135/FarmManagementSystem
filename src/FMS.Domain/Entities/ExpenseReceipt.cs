using FMS.Domain.Common;

namespace FMS.Domain.Entities;

/// <summary>
/// One file attached to an expense as proof of payment — a photographed paper receipt, an
/// invoice, a bank transfer confirmation.
///
/// <para>
/// A child row rather than columns on <see cref="Expense"/> because one purchase routinely
/// has more than one piece of paper, and because the storage metadata here is exactly the
/// set <see cref="AnimalDocument"/> already carries. Like that entity it is hard-deleted
/// when the user removes the file; the receipt needs no tombstone of its own because the
/// only way to reach one is through an expense, and an expense is never hard-deleted.
/// </para>
/// </summary>
public class ExpenseReceipt : AuditableEntity
{
    public Guid ExpenseId { get; set; }
    public Guid FarmId { get; set; }

    /// <summary>Key in the configured file store (local folder or object-storage key).</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>Randomised name actually stored, never trusted from the client.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The name the user's file had, shown in the UI and on download.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }

    public string? Description { get; set; }

    public Expense Expense { get; set; } = null!;
}