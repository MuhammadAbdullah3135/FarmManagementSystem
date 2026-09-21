namespace FMS.Application.Reports;

/// <summary>
/// The window the cost report covers. Both ends are inclusive dates, and leaving both
/// empty means the same default the feed report uses (the last 30 days) rather than the
/// health report's unbounded window — one report cannot have two.
/// </summary>
public class CostReportFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

/// <summary>
/// Cost and revenue per animal, and the same numbers rolled up per herd (a location),
/// for one date range.
/// </summary>
/// <remarks>
/// <para>
/// This report exists to answer "what did this animal cost, and what did it earn" without
/// inventing a number. Every figure is either a fact (a record that already names the
/// animal) or a single, stated fraction of a pool (<c>animal-days ÷ pool animal-days ×
/// pool</c>), and the fraction's three inputs travel with the amount so the UI can show
/// the arithmetic rather than assert a result.
/// </para>
/// <para>
/// <see cref="Reconciliation"/> and <see cref="Warnings"/> are not decoration: the report
/// states which money it could not attribute rather than absorbing it, and names the cost
/// that exists in the records but not in the expense ledger (feed consumption, and
/// labour) rather than appearing to contradict the profit-and-loss report.
/// </para>
/// </remarks>
public class CostPerAnimalReportDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    /// <summary>Days in the range that any animal was present, summed across animals.</summary>
    public int TotalAnimalDays { get; set; }

    /// <summary>Every animal the range touched: present in it, or holding money in it.</summary>
    public List<AnimalCostRowDto> Animals { get; set; } = new();

    /// <summary>The same rows grouped by the animal's current location. Provably their sum.</summary>
    public List<HerdCostRowDto> Herds { get; set; } = new();

    /// <summary>The whole farm's totals for the range — the sum of <see cref="Animals"/>.</summary>
    public CostTotalsDto Farm { get; set; } = new();

    /// <summary>The rules this report applied, so the UI can explain itself from one source.</summary>
    public List<CostRuleDto> Rules { get; set; } = new();

    public List<CostReportWarningDto> Warnings { get; set; } = new();

    public CostReconciliationDto Reconciliation { get; set; } = new();
}

public class CostTotalsDto
{
    public decimal TotalCost { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal Margin { get; set; }

    /// <summary>Cost divided by animal-days, or zero when nothing was present in the range.</summary>
    public decimal CostPerAnimalDay { get; set; }
}

public class HerdCostRowDto
{
    /// <summary>Null for the "not in a location" group.</summary>
    public Guid? LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public int AnimalCount { get; set; }
    public int AnimalDays { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal Margin { get; set; }
}

public class AnimalCostRowDto
{
    public Guid AnimalId { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }

    /// <summary>Where the animal is now. The herd table groups on this.</summary>
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }

    /// <summary>First day of presence in the range, after clamping to the range.</summary>
    public DateTime PresentFrom { get; set; }

    /// <summary>Last day of presence, or null when the animal is still present.</summary>
    public DateTime? PresentTo { get; set; }

    public int AnimalDays { get; set; }

    /// <summary>This animal's animal-days as a fraction of the farm's, for the range.</summary>
    public decimal ShareOfFarmDays { get; set; }

    public List<CostComponentDto> Costs { get; set; } = new();
    public List<CostComponentDto> Revenue { get; set; } = new();

    public decimal TotalCost { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal Margin { get; set; }

    /// <summary>Warning codes that apply to this animal specifically.</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// One line of a row's cost or revenue: either a record that named the animal, or a share
/// of a pool.
/// </summary>
public class CostComponentDto
{
    /// <summary>Stable key, e.g. <c>feed.direct</c>. See <see cref="CostComponentKeys"/>.</summary>
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Where the money comes from, in words: "Feed records naming this animal".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>How it was attributed. See <see cref="CostAllocationMethods"/>.</summary>
    public string Method { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public int RecordCount { get; set; }

    // ── allocation inputs, present only when Method is not "direct" ──
    // All three are what the UI shows as "10 / 60 days × 600.00".

    public decimal? PoolAmount { get; set; }
    public int? AllocatedDays { get; set; }
    public int? PoolDays { get; set; }

    /// <summary>
    /// The cent or two of rounding that landed on this row, so the arithmetic on screen
    /// adds up. Non-zero only for the largest-share holder of a pool whose exact shares do
    /// not divide into whole cents.
    /// </summary>
    public decimal RoundingAdjustment { get; set; }
}

/// <summary>One allocation rule, stated in words for the report's "how this was built" panel.</summary>
public class CostRuleDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class CostReportWarningDto
{
    /// <summary>Stable code. See <see cref="CostWarningCodes"/>.</summary>
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>How many records or animals the statement is about, where that applies.</summary>
    public int AffectedCount { get; set; }

    /// <summary>Money the statement refers to, where that applies.</summary>
    public decimal? Amount { get; set; }
}

/// <summary>
/// The report's arithmetic, stated so it can be checked: what each source holds in the
/// range, how much of it reached an animal row, and what did not.
/// </summary>
public class CostReconciliationDto
{
    // ── expenses: the ledger the profit-and-loss report reads ──

    /// <summary>Every non-deleted expense in the range — the same figure the P&amp;L sums.</summary>
    public decimal FarmExpensesTotal { get; set; }

    /// <summary>Expenses created by a health record in the range, counted from the health records instead.</summary>
    public decimal HealthLinkedExpenses { get; set; }

    public decimal ExpensesAttributedToAnimals { get; set; }
    public decimal ExpensesAllocatedFromLocations { get; set; }
    public decimal ExpensesAllocatedFromFarmPool { get; set; }
    public decimal ExpensesUnallocated { get; set; }

    /// <summary>True when every expense in the range is in exactly one of the buckets above.</summary>
    public bool ExpensesReconcile { get; set; }

    // ── feed: the cost the expense ledger never sees ──

    /// <summary>What the feed report totals for the same range.</summary>
    public decimal FeedConsumedTotal { get; set; }
    public decimal FeedAttributedToAnimals { get; set; }
    public decimal FeedAllocatedFromLocations { get; set; }
    public decimal FeedUnallocated { get; set; }
    public bool FeedReconciles { get; set; }

    // ── health ──

    /// <summary>What the health cost report totals for the same range.</summary>
    public decimal HealthRecordsTotal { get; set; }
    public decimal HealthAttributedToAnimals { get; set; }

    /// <summary>
    /// Health cost with no expense behind it (a record entered before the auto-expense
    /// existed, or one whose expense was removed). It is in this report and not in the P&amp;L.
    /// </summary>
    public decimal HealthCostOutsideTheExpenseLedger { get; set; }
    public bool HealthReconciles { get; set; }

    // ── labour ──

    public decimal LabourTotal { get; set; }

    /// <summary>
    /// Cost in this report that has no expense row at all: feed consumption and labour.
    /// This is why the report's total is deliberately larger than the P&amp;L's expense line.
    /// </summary>
    public decimal CostOutsideTheExpenseLedger { get; set; }
}

public static class CostComponentKeys
{
    public const string FeedDirect = "feed.direct";
    public const string FeedLocation = "feed.location";
    public const string HealthRecords = "health.records";
    public const string ExpenseDirect = "expense.direct";
    public const string ExpenseLocation = "expense.location";
    public const string ExpenseFarm = "expense.farm";
    public const string LabourFarm = "labour.farm";

    public const string IncomeDirect = "income.direct";
    public const string IncomeLocation = "income.location";
    public const string IncomeFarm = "income.farm";
}

public static class CostAllocationMethods
{
    /// <summary>The record itself named the animal. No arithmetic, no estimate.</summary>
    public const string Direct = "direct";

    /// <summary>Shared by the animals that spent time in one location, in proportion to days there.</summary>
    public const string LocationAnimalDays = "location-animal-days";

    /// <summary>Shared by every animal present in the range, in proportion to days present.</summary>
    public const string FarmAnimalDays = "farm-animal-days";
}

public static class CostWarningCodes
{
    /// <summary>No feed records in the range: the feed figures are absent, not estimated.</summary>
    public const string FeedNotRecorded = "feed.not-recorded";

    /// <summary>A pool could not be spread because nothing was present to spread it over.</summary>
    public const string PoolUnallocated = "pool.unallocated";

    /// <summary>No salary payments in the range, so labour cost is zero because nothing was recorded.</summary>
    public const string LabourNotRecorded = "labour.not-recorded";

    /// <summary>An animal counted as present from the start of the range because its entry date is unknown.</summary>
    public const string PresenceStartUnknown = "animal.presence-start-unknown";

    /// <summary>
    /// An animal whose status says it left, but no departure date could be read, so its
    /// animal-days — and therefore its share of every pool — are overstated.
    /// </summary>
    public const string DepartureUnknown = "animal.departure-unknown";

    /// <summary>Medicine issued in the range carries no cost anywhere, so it is not in these numbers.</summary>
    public const string MedicineCostNotRecorded = "excluded.medicine-cost";

    /// <summary>Inventory drawn down in the range carries no cost; purchases are in the expense ledger.</summary>
    public const string InventoryConsumptionNotCosted = "excluded.inventory-consumption";

    /// <summary>Feed purchases are excluded: consumption is the cost basis, so buying and feeding are not counted twice.</summary>
    public const string FeedPurchasesExcluded = "excluded.feed-purchases";

    /// <summary>Money that reached no animal row. Should always be zero; stated when it is not.</summary>
    public const string RowsUnassigned = "rows.unassigned";
}
