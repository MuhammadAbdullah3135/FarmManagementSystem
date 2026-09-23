using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class WeightRecord : AuditableEntity
{
    public Guid AnimalId { get; set; }
    public Guid FarmId { get; set; }
    public decimal WeightKg { get; set; }
    public DateTime RecordedAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// The offline device's id for the mutation that produced this row, when it arrived
    /// through the sync endpoint. Null for everything recorded live.
    ///
    /// Uniquely indexed per farm together with <see cref="FarmId"/>, so a device that
    /// retries a mutation the server already applied is refused by the database rather
    /// than silently producing a second measurement (this index used to be the only
    /// thing standing between a retry and a duplicate: <see cref="RecordedAt"/> is
    /// deliberately non-unique, because two weights at the same timestamp can both be real).
    /// </summary>
    public Guid? ClientMutationId { get; set; }

    public Animal Animal { get; set; } = null!;
}
