namespace FMS.Application.Health;

/// <summary>
/// One farm's share of a vet backfill. The totals on
/// <see cref="VetBackfillReport"/> are the sums of these rows.
/// </summary>
public class VetBackfillFarmReport
{
    public Guid FarmId { get; set; }
    public int VetsCreated { get; set; }
    public int MedicalRecordsLinked { get; set; }
    public int VaccinationRecordsLinked { get; set; }
    public int BreedingRecordsLinked { get; set; }
    public int BirthRecordsLinked { get; set; }
    public int BlankVetNamesLeftNull { get; set; }
}

/// <summary>
/// What a vet backfill run did, or - for a dry run - what it would do. Running the
/// same backfill again reports zero rows created and zero records linked, because it
/// only considers clinical records whose <c>VetId</c> is still null.
/// </summary>
public class VetBackfillReport
{
    public bool Applied { get; set; }
    public int VetsCreated { get; set; }
    public int MedicalRecordsLinked { get; set; }
    public int VaccinationRecordsLinked { get; set; }
    public int BreedingRecordsLinked { get; set; }
    public int BirthRecordsLinked { get; set; }
    public int BlankVetNamesLeftNull { get; set; }
    public List<VetBackfillFarmReport> Farms { get; set; } = new();
}
