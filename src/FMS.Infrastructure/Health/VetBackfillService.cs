using FMS.Application.Health;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Health;

/// <summary>
/// Creates one vet per distinct, non-empty clinical vet name in each farm and links
/// the records that do not have a <c>VetId</c> yet. It never changes the retained
/// <c>VetName</c>, never overwrites an existing link, includes soft-deleted medical
/// records, and leaves blank/null names null, so a second run is a no-op.
/// </summary>
public class VetBackfillService : IVetBackfillService
{
    private readonly FmsDbContext _context;

    public VetBackfillService(FmsDbContext context)
    {
        _context = context;
    }

    public async Task<VetBackfillReport> RunAsync(
        bool apply, CancellationToken cancellationToken = default)
    {
        if (apply)
            return await RunCoreAsync(apply: true, cancellationToken);

        // Same belt-and-braces read-only handling as the inventory-category backfill:
        // report without staging changes, and roll back a transaction even on providers
        // that would happily let a future edit write. PostgreSQL also gets READ ONLY.
        if (!_context.Database.IsRelational())
            return await RunCoreAsync(apply: false, cancellationToken);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (_context.Database.IsNpgsql())
                await _context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);

            var report = await RunCoreAsync(apply: false, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return report;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<VetBackfillReport> RunCoreAsync(
        bool apply, CancellationToken cancellationToken)
    {
        // Only unlinked records matter, so a second run sees an empty set. The whitespace
        // test is done in memory so the result does not depend on provider null semantics.
        var medicalRecords = await _context.MedicalRecords
            .IgnoreQueryFilters()
            .Where(m => m.VetId == null)
            .ToListAsync(cancellationToken);
        var vaccinationRecords = await _context.VaccinationRecords
            .Where(v => v.VetId == null)
            .ToListAsync(cancellationToken);
        var breedingRecords = await _context.BreedingRecords
            .Where(br => br.VetId == null)
            .ToListAsync(cancellationToken);
        var birthRecords = await _context.BirthRecords
            .Where(br => br.VetId == null)
            .ToListAsync(cancellationToken);

        var report = new VetBackfillReport { Applied = apply };
        var reportsByFarm = new Dictionary<Guid, VetBackfillFarmReport>();

        VetBackfillFarmReport FarmReport(Guid farmId)
        {
            if (!reportsByFarm.TryGetValue(farmId, out var row))
            {
                row = new VetBackfillFarmReport { FarmId = farmId };
                reportsByFarm[farmId] = row;
            }
            return row;
        }

        var pending = new List<PendingRecord>();

        void Collect(
            Guid farmId,
            string? vetName,
            PendingRecordKind kind,
            Action<Guid> assign)
        {
            if (string.IsNullOrWhiteSpace(vetName))
            {
                FarmReport(farmId).BlankVetNamesLeftNull++;
                return;
            }

            pending.Add(new PendingRecord(farmId, vetName.Trim(), kind, assign));
        }

        foreach (var record in medicalRecords)
            Collect(record.FarmId, record.VetName, PendingRecordKind.Medical, id => record.VetId = id);
        foreach (var record in vaccinationRecords)
            Collect(record.FarmId, record.VetName, PendingRecordKind.Vaccination, id => record.VetId = id);
        foreach (var record in breedingRecords)
            Collect(record.FarmId, record.VetName, PendingRecordKind.Breeding, id => record.VetId = id);
        foreach (var record in birthRecords)
            Collect(record.FarmId, record.VetName, PendingRecordKind.Birth, id => record.VetId = id);

        var existingVets = await _context.Vets.ToListAsync(cancellationToken);

        // Keyed by farm and normalized name. A vet created earlier in this run is also
        // inserted here so differently-cased spellings in the same farm share one row.
        var vetsByKey = new Dictionary<(Guid FarmId, string NormalizedName), Vet>();
        foreach (var vet in existingVets)
        {
            var key = (vet.FarmId, Normalize(vet.Name));
            if (!vetsByKey.ContainsKey(key))
                vetsByKey[key] = vet;
        }

        foreach (var group in pending.GroupBy(record => new
                 {
                     record.FarmId,
                     NormalizedName = Normalize(record.VetName)
                 }))
        {
            var farmReport = FarmReport(group.Key.FarmId);
            var key = (group.Key.FarmId, group.Key.NormalizedName);

            if (!vetsByKey.TryGetValue(key, out var vet))
            {
                // The proposal's deterministic spelling: case-insensitive first, then
                // ordinal, so the same row wins on every run.
                var name = group
                    .Select(record => record.VetName)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value, StringComparer.Ordinal)
                    .First();

                vet = new Vet
                {
                    Id = Guid.NewGuid(),
                    FarmId = group.Key.FarmId,
                    Name = name,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                vetsByKey[key] = vet;
                if (apply)
                    _context.Vets.Add(vet);

                report.VetsCreated++;
                farmReport.VetsCreated++;
            }
        }

        foreach (var record in pending)
        {
            if (!vetsByKey.TryGetValue(
                    (record.FarmId, Normalize(record.VetName)), out var vet))
                continue;

            var farmReport = FarmReport(record.FarmId);
            switch (record.Kind)
            {
                case PendingRecordKind.Medical:
                    report.MedicalRecordsLinked++;
                    farmReport.MedicalRecordsLinked++;
                    break;
                case PendingRecordKind.Vaccination:
                    report.VaccinationRecordsLinked++;
                    farmReport.VaccinationRecordsLinked++;
                    break;
                case PendingRecordKind.Breeding:
                    report.BreedingRecordsLinked++;
                    farmReport.BreedingRecordsLinked++;
                    break;
                case PendingRecordKind.Birth:
                    report.BirthRecordsLinked++;
                    farmReport.BirthRecordsLinked++;
                    break;
            }

            if (apply)
                record.Assign(vet.Id);
        }

        if (apply)
            await _context.SaveChangesAsync(cancellationToken);

        report.Farms = reportsByFarm.Values
            .OrderBy(row => row.FarmId)
            .ToList();
        report.BlankVetNamesLeftNull = report.Farms.Sum(row => row.BlankVetNamesLeftNull);
        return report;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private enum PendingRecordKind
    {
        Medical,
        Vaccination,
        Breeding,
        Birth
    }

    private sealed record PendingRecord(
        Guid FarmId,
        string VetName,
        PendingRecordKind Kind,
        Action<Guid> Assign);
}
