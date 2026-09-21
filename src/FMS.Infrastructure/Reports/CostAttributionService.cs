using FMS.Application.Common;
using FMS.Application.Feed;
using FMS.Application.Health;
using FMS.Application.Reports;
using FMS.Domain.Common;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FMS.Infrastructure.Reports;

/// <summary>
/// Cost and revenue per animal, and the same numbers rolled up by herd (location).
///
/// <para>
/// The governing rule of this service: a number is either a <b>fact</b> — a record that
/// already names the animal — or a <b>stated fraction of a pool</b>. There is no scoring,
/// no weighting and no estimate anywhere. Every allocated figure can be read as
/// <c>this animal's days ÷ the pool's days × the pool</c>, and those numbers travel with
/// the amount so the UI can show the arithmetic instead of asking the user to trust a
/// result.
/// </para>
///
/// <para>
/// Three scopes, applied in order, with nothing counted twice:
/// <list type="number">
/// <item><b>Direct</b> — the record names the animal: a feed record with an animal, a
/// medical or vaccination record, or an expense or income row with <c>AnimalId</c>.</item>
/// <item><b>Location pool</b> — the record names a location: a group feed record, or an
/// expense or income row with <c>LocationId</c>. The pool is shared by the animals that
/// spent time in that location, in proportion to their days there, reconstructed from
/// <c>AnimalTransfer</c> so an animal that moved mid-range carries the right share of each
/// pen rather than all of its current one.</item>
/// <item><b>Farm pool</b> — the record names neither: salary payments, and expenses and
/// income with no attribution. Shared by every animal present in the range, in proportion
/// to days present.</item>
/// </list>
/// </para>
///
/// <para>
/// Two things this service deliberately does not do, because both would invent numbers: it
/// does not cost medicine issued or inventory drawn down (neither records a cost anywhere,
/// so costing them means guessing at a price), and it does not add feed <i>purchases</i> on
/// top of feed <i>consumption</i> (consumption is the cost basis; adding purchases would
/// count the same feed twice). All three are named in the report's warnings instead, with
/// record counts, whenever the records exist.
/// </para>
///
/// <para>
/// And one thing it insists on: money and revenue that reach no animal row are <b>reported
/// as unallocated</b> with the reason, never absorbed into the rows.
/// <see cref="CostReconciliationDto"/> states what each source holds in the range and how
/// much of it landed on a row, so the report can be checked against the feed, health and
/// finance figures it borrows.
/// </para>
///
/// <para>
/// Where the numbers come from matters as much as how they are split: the per-animal feed
/// cost is <see cref="IFeedService"/>'s own figure and the per-animal health cost is
/// <see cref="IHealthCostService"/>'s, so this report cannot disagree with the feed and
/// health reports for the same range. Tests assert those equalities rather than trusting
/// them.
/// </para>
/// </summary>
public class CostAttributionService : ICostAttributionService
{
    /// <summary>Default window when neither end is given — the same default the feed report uses.</summary>
    public const int DefaultRangeDays = 30;

    private readonly FmsDbContext _context;
    private readonly IFeedService _feed;
    private readonly IHealthCostService _healthCost;
    private readonly ILogger<CostAttributionService> _logger;

    public CostAttributionService(
        FmsDbContext context,
        IFeedService feed,
        IHealthCostService healthCost,
        ILogger<CostAttributionService> logger)
    {
        _context = context;
        _feed = feed;
        _healthCost = healthCost;
        _logger = logger;
    }

    public async Task<Result<CostPerAnimalReportDto>> GetCostPerAnimalReportAsync(Guid farmId, CostReportFilter filter)
    {
        if (!TryResolveRange(filter, out var from, out var to))
            return Result<CostPerAnimalReportDto>.Validation("From date must be before or equal to To date");

        var toExclusive = to.AddDays(1);

        // ── the figures this report borrows rather than re-derives ──
        // Each is the owning report's own number, so a disagreement between reports is
        // impossible rather than merely unlikely.

        var feedByAnimal = await _feed.GetConsumptionByAnimalAsync(farmId, from, to);
        if (!feedByAnimal.IsSuccess)
            return Result<CostPerAnimalReportDto>.Failure(feedByAnimal.Error!);

        var feedByLocation = await _feed.GetConsumptionByLocationAsync(farmId, from, to);
        if (!feedByLocation.IsSuccess)
            return Result<CostPerAnimalReportDto>.Failure(feedByLocation.Error!);

        var feedSummary = await _feed.GetCostSummaryAsync(farmId, from, to);
        if (!feedSummary.IsSuccess)
            return Result<CostPerAnimalReportDto>.Failure(feedSummary.Error!);

        var healthFilter = new HealthCostFilter { From = from, To = to };
        var healthByAnimal = await _healthCost.GetCostsByAnimalAsync(farmId, healthFilter);
        if (!healthByAnimal.IsSuccess)
            return Result<CostPerAnimalReportDto>.Failure(healthByAnimal.Error!);

        var healthSummary = await _healthCost.GetCostSummaryAsync(farmId, healthFilter);
        if (!healthSummary.IsSuccess)
            return Result<CostPerAnimalReportDto>.Failure(healthSummary.Error!);

        // ── the raw material ──

        var animals = await LoadAnimalsAsync(farmId);

        var statuses = await _context.AnimalStatuses
            .Where(status => status.FarmId == farmId)
            .Select(status => new StatusFact(status.Id, status.Name, status.Category))
            .ToListAsync();

        var terminalStatusIds = statuses
            .Where(status => status.Category == AnimalStatusCategory.Terminal)
            .Select(status => status.Id)
            .ToHashSet();

        var terminalStatusNames = statuses
            .Where(status => status.Category == AnimalStatusCategory.Terminal)
            .Select(status => status.Name.Trim())
            .Where(name => name.Length > 0)
            .ToList();

        var statusEvents = await _context.AnimalTimelineEvents
            .Where(evt => evt.FarmId == farmId && evt.EventType == TimelineEventTypes.StatusChanged)
            .Select(evt => new StatusEventFact(evt.AnimalId, evt.OccurredAt, evt.Title, evt.RelatedEntityId))
            .ToListAsync();

        var transfers = await _context.AnimalTransfers
            .Where(transfer => transfer.FarmId == farmId)
            .Select(transfer => new TransferFact(
                transfer.AnimalId, transfer.FromLocationId, transfer.ToLocationId, transfer.TransferredAt))
            .ToListAsync();

        // Expenses created by a health record *in this range* are counted from that health
        // record instead: the health module writes both, so counting both would double every
        // vet bill. A health record outside the range leaves its expense in the pools below.
        var healthLinkedExpenseIds = await LoadHealthLinkedExpenseIdsAsync(farmId, from, toExclusive);

        var expenses = await _context.Expenses
            .Where(expense => expense.FarmId == farmId && !expense.IsDeleted
                && expense.ExpenseDate >= from && expense.ExpenseDate < toExclusive)
            .Select(expense => new ExpenseFact(expense.Id, expense.AnimalId, expense.LocationId, expense.Amount))
            .ToListAsync();

        var income = await _context.IncomeRecords
            .Where(record => record.FarmId == farmId
                && record.IncomeDate >= from && record.IncomeDate < toExclusive)
            .Select(record => new IncomeFact(record.AnimalId, record.LocationId, record.Amount))
            .ToListAsync();

        var labour = await _context.SalaryPayments
            .Where(payment => payment.FarmId == farmId
                && payment.PaymentDate >= from && payment.PaymentDate < toExclusive)
            .SumAsync(payment => payment.Amount);

        // Record counts for the transparency columns. The costs themselves come from the
        // feed service; these are only how many records produced them.
        var feedDirectCounts = await _context.FeedRecords
            .Where(record => record.FarmId == farmId && record.AnimalId != null
                && record.FedAt >= from && record.FedAt < toExclusive)
            .GroupBy(record => record.AnimalId!.Value)
            .Select(group => new CountFact(group.Key, group.Count()))
            .ToListAsync();

        var feedLocationCounts = await _context.FeedRecords
            .Where(record => record.FarmId == farmId && record.LocationId != null
                && record.FedAt >= from && record.FedAt < toExclusive)
            .GroupBy(record => record.LocationId!.Value)
            .Select(group => new CountFact(group.Key, group.Count()))
            .ToListAsync();

        // ── presence, and the animal-days every allocation rests on ──

        var eventsByAnimal = statusEvents
            .GroupBy(evt => evt.AnimalId)
            .ToDictionary(group => group.Key, group => group.OrderBy(evt => evt.OccurredAt).ToList());

        var transfersByAnimal = transfers
            .GroupBy(transfer => transfer.AnimalId)
            .ToDictionary(group => group.Key, group => group.OrderBy(transfer => transfer.TransferredAt).ToList());

        var feedByAnimalLookup = feedByAnimal.Value!.ToDictionary(item => item.AnimalId);
        var healthByAnimalLookup = healthByAnimal.Value!.ToDictionary(item => item.AnimalId);
        var directFeedCounts = feedDirectCounts.ToDictionary(item => item.Key, item => item.Count);
        var locationFeedCounts = feedLocationCounts.ToDictionary(item => item.Key, item => item.Count);

        var rows = new List<RowBuilder>();
        var presenceStartUnknown = 0;
        var departureUnknown = 0;

        foreach (var animal in animals)
        {
            var presence = ResolvePresence(
                animal, from, to, terminalStatusIds, terminalStatusNames,
                eventsByAnimal.GetValueOrDefault(animal.Id) ?? new List<StatusEventFact>());

            if (presence.StartIsUnknown)
                presenceStartUnknown++;

            if (presence.DepartureIsUnknown)
                departureUnknown++;

            var feeds = feedByAnimalLookup.GetValueOrDefault(animal.Id);
            var health = healthByAnimalLookup.GetValueOrDefault(animal.Id);
            var animalExpenses = expenses
                .Where(expense => expense.AnimalId == animal.Id && !healthLinkedExpenseIds.Contains(expense.Id))
                .ToList();
            var animalIncome = income.Where(record => record.AnimalId == animal.Id).ToList();

            // An animal earns a row if it was here, or if any record in this range names it.
            // The second half matters: an animal sold the day it was vaccinated has no days
            // in a later range, and dropping its row would drop its money.
            var hasMoney = feeds is not null || health is not null
                || animalExpenses.Count > 0 || animalIncome.Count > 0;

            if (presence.Days == 0 && !hasMoney)
                continue;

            rows.Add(new RowBuilder
            {
                Animal = animal,
                PresenceFrom = presence.From,
                PresenceTo = presence.To,
                Days = presence.Days,
                LocationDays = ResolveLocationDays(animal, presence,
                    transfersByAnimal.TryGetValue(animal.Id, out var animalTransfers) ? animalTransfers : new List<TransferFact>()),
                FeedCost = feeds?.Cost ?? 0m,
                FeedRecords = directFeedCounts.GetValueOrDefault(animal.Id),
                HealthCost = health?.TotalCost ?? 0m,
                HealthRecords = health?.RecordCount ?? 0,
                ExpenseCost = animalExpenses.Sum(expense => expense.Amount),
                ExpenseRecords = animalExpenses.Count,
                Income = animalIncome.Sum(record => record.Amount),
                IncomeRecords = animalIncome.Count,
                StartIsUnknown = presence.StartIsUnknown,
                DepartureIsUnknown = presence.DepartureIsUnknown
            });
        }

        var totalAnimalDays = rows.Sum(row => row.Days);

        // ── the pools, and the arithmetic that spreads them ──

        var expenseFarmPool = expenses
            .Where(expense => !healthLinkedExpenseIds.Contains(expense.Id)
                && expense.AnimalId is null && expense.LocationId is null)
            .Sum(expense => expense.Amount);

        var expenseLocationPools = expenses
            .Where(expense => !healthLinkedExpenseIds.Contains(expense.Id)
                && expense.AnimalId is null && expense.LocationId is not null)
            .GroupBy(expense => expense.LocationId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(expense => expense.Amount));

        var incomeFarmPool = income
            .Where(record => record.AnimalId is null && record.LocationId is null)
            .Sum(record => record.Amount);

        var incomeLocationPools = income
            .Where(record => record.AnimalId is null && record.LocationId is not null)
            .GroupBy(record => record.LocationId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(record => record.Amount));

        var unallocated = new UnallocatedBuckets();

        // Location pools: the denominator is the days the animals spent in that location.
        foreach (var (locationId, pool) in (feedByLocation.Value ?? new List<LocationConsumptionDto>())
                     .ToDictionary(item => item.LocationId, item => item.Cost))
            unallocated.FeedLocation += SpreadLocationPool(rows, locationId, pool,
                (row, days, poolDays, total, amount, rounding) => row.AddFeedLocation(days, poolDays, total, amount, rounding));

        foreach (var (locationId, pool) in expenseLocationPools)
            unallocated.ExpenseLocation += SpreadLocationPool(rows, locationId, pool,
                (row, days, poolDays, total, amount, rounding) => row.AddExpenseLocation(days, poolDays, total, amount, rounding));

        foreach (var (locationId, pool) in incomeLocationPools)
            unallocated.IncomeLocation += SpreadLocationPool(rows, locationId, pool,
                (row, days, poolDays, total, amount, rounding) => row.AddIncomeLocation(days, poolDays, total, amount, rounding));

        // Farm pools: the denominator is every animal-day in the range. With nothing present
        // there is no basis to divide by, so the money is reported unallocated — not divided
        // by zero, and not quietly dropped.
        void SpreadFarmPool(decimal pool, Func<RowBuilder, int, int, decimal, decimal, decimal, bool> apply)
        {
            if (pool == 0m)
                return;

            Spread(rows.Select(row => row.Days).ToList(), pool, (index, amount, rounding) =>
                apply(rows[index], rows[index].Days, totalAnimalDays, pool, amount, rounding));
        }

        if (totalAnimalDays == 0)
        {
            unallocated.Labour = labour;
            unallocated.ExpenseFarm = expenseFarmPool;
            unallocated.IncomeFarm = incomeFarmPool;
        }
        else
        {
            SpreadFarmPool(labour, (row, days, poolDays, total, amount, rounding) =>
                row.AddLabour(days, poolDays, total, amount, rounding));
            SpreadFarmPool(expenseFarmPool, (row, days, poolDays, total, amount, rounding) =>
                row.AddExpenseFarm(days, poolDays, total, amount, rounding));
            SpreadFarmPool(incomeFarmPool, (row, days, poolDays, total, amount, rounding) =>
                row.AddIncomeFarm(days, poolDays, total, amount, rounding));
        }

        // ── assembly ──

        var report = new CostPerAnimalReportDto
        {
            From = from,
            To = to,
            TotalAnimalDays = totalAnimalDays,
            Animals = rows.Select(row => row.ToDto(totalAnimalDays)).ToList(),
            Rules = BuildRules()
        };

        report.Warnings.AddRange(await BuildWarningsAsync(
            farmId, from, toExclusive,
            feedSummary.Value!.TotalConsumedCost, labour, unallocated, presenceStartUnknown, departureUnknown));

        report.Reconciliation = BuildReconciliation(
            expenses, healthLinkedExpenseIds, report.Animals, labour,
            feedSummary.Value.TotalConsumedCost, healthSummary.Value!.GrandTotal, unallocated);

        // Money in a source total that reached neither a row nor an unallocated bucket is a
        // defect in this report, not a rounding detail, so it is stated rather than hidden.
        var unassigned = UnassignedMoney(report.Reconciliation);
        if (unassigned != 0m)
        {
            report.Warnings.Add(new CostReportWarningDto
            {
                Code = CostWarningCodes.RowsUnassigned,
                Message = "Some of this range's cost reached no animal row. These figures are incomplete "
                    + "and should be investigated rather than relied on.",
                Amount = unassigned
            });
        }

        report.Herds = BuildHerds(report.Animals);
        report.Farm = new CostTotalsDto
        {
            TotalCost = report.Animals.Sum(row => row.TotalCost),
            TotalRevenue = report.Animals.Sum(row => row.TotalRevenue),
            Margin = report.Animals.Sum(row => row.Margin),
            CostPerAnimalDay = totalAnimalDays > 0
                ? Math.Round(report.Animals.Sum(row => row.TotalCost) / totalAnimalDays, 2)
                : 0m
        };

        _logger.LogInformation(
            "Cost report for farm {FarmId} {From:yyyy-MM-dd}..{To:yyyy-MM-dd}: {AnimalCount} animals, {AnimalDays} animal-days, cost {TotalCost}, revenue {TotalRevenue}, unallocated {Unallocated}",
            farmId, from, to, report.Animals.Count, totalAnimalDays,
            report.Farm.TotalCost, report.Farm.TotalRevenue, unallocated.Total);

        return Result<CostPerAnimalReportDto>.Success(report);
    }

    // ── range ───────────────────────────────────────────────

    /// <summary>Inclusive dates, defaulting to the feed report's window when neither end is given.</summary>
    private static bool TryResolveRange(CostReportFilter filter, out DateTime from, out DateTime to)
    {
        to = (filter.To ?? DateTime.UtcNow).Date;
        from = (filter.From ?? to.AddDays(-DefaultRangeDays)).Date;
        return from <= to;
    }

    // ── presence ────────────────────────────────────────────

    /// <summary>
    /// The days an animal was in the herd during the range.
    ///
    /// The start is the acquisition date, or the date of birth when there is no acquisition
    /// date; with neither, the animal counts as present from the start of the range and the
    /// report says so, because the alternative — excluding it — would drop its costs.
    ///
    /// The end is the day the animal left: the first status change into a status the farm
    /// marks terminal, read from the event's related id where it has one and, for rows
    /// written before that id was stored, from the event's own title; or the day it was
    /// deleted. The day it left counts as present — it was there that morning.
    /// </summary>
    private static Presence ResolvePresence(
        AnimalFact animal,
        DateTime rangeFrom,
        DateTime rangeTo,
        HashSet<Guid> terminalStatusIds,
        List<string> terminalStatusNames,
        List<StatusEventFact> statusEvents)
    {
        var startIsUnknown = (animal.AcquisitionDate ?? animal.DateOfBirth) is null;
        var start = (animal.AcquisitionDate ?? animal.DateOfBirth)?.Date ?? rangeFrom;

        var terminalEvent = statusEvents.FirstOrDefault(evt =>
            evt.RelatedEntityId.HasValue && terminalStatusIds.Contains(evt.RelatedEntityId.Value));

        // Historical rows predate the related id this report relies on, so the event's own
        // title — always written as "Old → New" from the farm's status names — is the
        // fallback. It can miss if a status was renamed after the fact, which is exactly why
        // an unresolved departure is reported rather than assumed.
        terminalEvent ??= statusEvents.FirstOrDefault(evt =>
            terminalStatusNames.Any(name => evt.Title.EndsWith($"→ {name}", StringComparison.OrdinalIgnoreCase)));

        var departure = terminalEvent?.OccurredAt.Date ?? animal.DeletedAt?.Date;
        var departureIsUnknown = departure is null && animal.StatusCategory == AnimalStatusCategory.Terminal;

        var from = start > rangeFrom ? start : rangeFrom;
        var lastDay = departure.HasValue && departure.Value < rangeTo ? departure.Value : rangeTo;
        var days = lastDay >= from ? (lastDay - from).Days + 1 : 0;

        // The window the row reports is the window inside the range; a departure after it is
        // not this range's business, and null means "still here". LastDay is the same last day
        // without that null meaning anything — the location timeline needs an end date even
        // for an animal that never left, and a range end is the honest one.
        return new Presence(
            From: from,
            To: days == 0 ? null : departure.HasValue && departure.Value <= rangeTo ? departure.Value : null,
            LastDay: lastDay,
            Days: days,
            StartIsUnknown: startIsUnknown,
            DepartureIsUnknown: departureIsUnknown);
    }

    /// <summary>
    /// The days an animal spent in each location, so a pen's shared feed is spread over the
    /// animals that were actually in that pen.
    ///
    /// The location timeline is reconstructed from the transfer log: before the first
    /// transfer the animal was where that transfer came from, and the day of a transfer
    /// counts to the destination. With no transfers in range, the animal's current location
    /// covers the whole span. The segment days always sum to the animal's animal-days.
    /// </summary>
    private static Dictionary<Guid, int> ResolveLocationDays(
        AnimalFact animal, Presence presence, List<TransferFact> transfers)
    {
        var days = new Dictionary<Guid, int>();
        if (presence.Days == 0)
            return days;

        var rangeEnd = presence.LastDay;

        void Add(Guid? locationId, int count)
        {
            if (locationId is not { } id || count <= 0)
                return;

            days[id] = days.GetValueOrDefault(id) + count;
        }

        var within = transfers
            .Where(transfer => transfer.TransferredAt.Date >= presence.From && transfer.TransferredAt.Date <= rangeEnd)
            .ToList();

        if (within.Count == 0)
        {
            Add(animal.LocationId, presence.Days);
            return days;
        }

        var cursor = presence.From;
        var current = within[0].FromLocationId;

        foreach (var transfer in within)
        {
            var cut = transfer.TransferredAt.Date;
            if (cut > cursor)
                Add(current, (cut - cursor).Days);

            current = transfer.ToLocationId;
            cursor = cut;
        }

        if (cursor <= rangeEnd)
            Add(current, (rangeEnd - cursor).Days + 1);

        return days;
    }

    // ── allocation ──────────────────────────────────────────

    /// <summary>
    /// Spreads one location's pool over the animals that spent days there. Returns the part
    /// that could not be spread — a location whose animals were never recorded in it.
    /// </summary>
    private static decimal SpreadLocationPool(
        List<RowBuilder> rows,
        Guid locationId,
        decimal pool,
        Func<RowBuilder, int, int, decimal, decimal, decimal, bool> apply)
    {
        if (pool == 0m)
            return 0m;

        var weights = rows.Select(row => row.LocationDays.GetValueOrDefault(locationId)).ToList();
        var denominator = weights.Sum();

        if (denominator == 0)
            return pool;

        // The callback is given this row's days in the location, the pool's total days there,
        // and the pool — the three numbers the UI shows as the arithmetic.
        Spread(weights, pool, (index, amount, rounding) =>
            apply(rows[index], weights[index], denominator, pool, amount, rounding));

        return 0m;
    }

    /// <summary>
    /// Splits a pool by weight so the parts add up to the pool <b>exactly</b>.
    ///
    /// Each share is rounded to cents and the remainder — at most a cent per animal — goes to
    /// the largest holder, with the adjustment reported on that row. Rounding each share
    /// independently would leave a reconciliation off by a cent, which in a report whose
    /// whole point is that the arithmetic adds up is not acceptable.
    /// </summary>
    private static void Spread(IReadOnlyList<int> weights, decimal pool, Action<int, decimal, decimal> apply)
    {
        var total = weights.Sum();
        if (total == 0 || pool == 0m)
            return;

        var amounts = new decimal[weights.Count];
        var allocated = 0m;

        for (var index = 0; index < weights.Count; index++)
        {
            if (weights[index] == 0)
                continue;

            amounts[index] = Math.Round(pool * weights[index] / total, 2, MidpointRounding.AwayFromZero);
            allocated += amounts[index];
        }

        var biggest = 0;
        for (var index = 1; index < weights.Count; index++)
        {
            if (weights[index] > weights[biggest])
                biggest = index;
        }

        var remainder = pool - allocated;
        if (remainder != 0m)
            amounts[biggest] += remainder;

        for (var index = 0; index < weights.Count; index++)
        {
            if (weights[index] == 0)
                continue;

            apply(index, amounts[index], index == biggest ? remainder : 0m);
        }
    }

    // ── rules, warnings, reconciliation ─────────────────────

    private static List<CostRuleDto> BuildRules() => new()
    {
        new CostRuleDto
        {
            Key = CostAllocationMethods.Direct,
            Title = "Records that name the animal are used as they are",
            Description = "A feed record with an animal, a medical or vaccination record, or an expense or "
                + "income row with an animal is that animal's cost or revenue. Nothing is shared, nothing is estimated."
        },
        new CostRuleDto
        {
            Key = CostAllocationMethods.LocationAnimalDays,
            Title = "A pen's costs are shared by the animals that were in it",
            Description = "Group feed, and expenses and income recorded against a location, are split between the "
                + "animals that spent time there, in proportion to the days each one spent there — read from the "
                + "transfer history, so an animal that moved mid-period carries the right share of each pen. "
                + "If no animal was recorded in that location in the range, the amount is shown as unallocated."
        },
        new CostRuleDto
        {
            Key = CostAllocationMethods.FarmAnimalDays,
            Title = "Whole-farm costs are shared by animal-days",
            Description = "Salary payments, and expenses and income recorded against neither an animal nor a "
                + "location, are split between every animal present in the range in proportion to the days each "
                + "one was present. Each row shows the fraction it received."
        },
        new CostRuleDto
        {
            Key = "exclusions",
            Title = "Some costs are named rather than estimated",
            Description = "Medicine issued and inventory drawn down carry no cost anywhere in this system, and feed "
                + "purchases are excluded because feed consumption is already counted. None of the three is estimated "
                + "here; each is listed as a caveat when the records exist."
        }
    };

    private async Task<List<CostReportWarningDto>> BuildWarningsAsync(
        Guid farmId,
        DateTime from,
        DateTime toExclusive,
        decimal feedConsumedTotal,
        decimal labour,
        UnallocatedBuckets unallocated,
        int presenceStartUnknown,
        int departureUnknown)
    {
        var warnings = new List<CostReportWarningDto>();

        if (feedConsumedTotal == 0m)
        {
            warnings.Add(new CostReportWarningDto
            {
                Code = CostWarningCodes.FeedNotRecorded,
                Message = "No feed consumption was recorded in this range, so feed cost is missing from these "
                    + "figures. It is absent rather than estimated."
            });
        }

        if (unallocated.Total != 0m)
        {
            warnings.Add(new CostReportWarningDto
            {
                Code = CostWarningCodes.PoolUnallocated,
                Message = "Some shared cost or revenue could not be attributed to an animal, because no animal was "
                    + "recorded as present for the pool it belongs to. It is shown as unallocated rather than spread "
                    + "silently.",
                Amount = Math.Round(unallocated.Total, 2)
            });
        }

        if (labour == 0m)
        {
            warnings.Add(new CostReportWarningDto
            {
                Code = CostWarningCodes.LabourNotRecorded,
                Message = "No salary payments were recorded in this range, so labour contributes nothing to these "
                    + "figures — which is not the same as labour having cost nothing."
            });
        }

        if (presenceStartUnknown > 0)
        {
            warnings.Add(new CostReportWarningDto
            {
                Code = CostWarningCodes.PresenceStartUnknown,
                Message = "Some animals have neither an acquisition date nor a date of birth, so they are counted as "
                    + "present for the whole range. Their share of shared costs is overstated.",
                AffectedCount = presenceStartUnknown
            });
        }

        if (departureUnknown > 0)
        {
            warnings.Add(new CostReportWarningDto
            {
                Code = CostWarningCodes.DepartureUnknown,
                Message = "Some animals are in a status that means they have left the herd, but no departure date "
                    + "could be read, so they are counted as present for the whole range. Their share of shared "
                    + "costs is overstated.",
                AffectedCount = departureUnknown
            });
        }

        // The named exclusions. Each is reported only when the records exist, with how many,
        // so "not included" is a stated decision rather than a silent omission.
        var medicineUsageCount = await _context.MedicineUsages
            .CountAsync(usage => usage.FarmId == farmId
                && usage.DateUsed >= from && usage.DateUsed < toExclusive);

        if (medicineUsageCount > 0)
        {
            warnings.Add(new CostReportWarningDto
            {
                Code = CostWarningCodes.MedicineCostNotRecorded,
                Message = "Medicine issued in this range records a quantity but no cost, so it is not priced here. "
                    + "Enter the cost on the medical record if you want it counted.",
                AffectedCount = medicineUsageCount
            });
        }

        var consumptionCount = await _context.StockMovements
            .CountAsync(movement => movement.FarmId == farmId
                && movement.MovementType == InventoryMovementType.Consumption
                && movement.MovementDate >= from && movement.MovementDate < toExclusive);

        if (consumptionCount > 0)
        {
            warnings.Add(new CostReportWarningDto
            {
                Code = CostWarningCodes.InventoryConsumptionNotCosted,
                Message = "Inventory was drawn down in this range. Those movements record no cost, so they are not "
                    + "priced here — the purchases behind them are in the expense figures instead.",
                AffectedCount = consumptionCount
            });
        }

        var feedPurchaseCount = await _context.FeedStockMovements
            .CountAsync(movement => movement.FarmId == farmId
                && movement.MovementType == StockMovementType.Purchase
                && movement.MovementDate >= from && movement.MovementDate < toExclusive);

        if (feedPurchaseCount > 0)
        {
            warnings.Add(new CostReportWarningDto
            {
                Code = CostWarningCodes.FeedPurchasesExcluded,
                Message = "Feed bought in this range is excluded: feed is costed when it is consumed, so counting "
                    + "the purchase as well would count the same feed twice.",
                AffectedCount = feedPurchaseCount
            });
        }

        return warnings;
    }

    private static CostReconciliationDto BuildReconciliation(
        List<ExpenseFact> expenses,
        HashSet<Guid> healthLinkedExpenseIds,
        List<AnimalCostRowDto> animals,
        decimal labour,
        decimal feedConsumedTotal,
        decimal healthRecordsTotal,
        UnallocatedBuckets unallocated)
    {
        decimal SumComponents(string key) => Math.Round(
            animals.SelectMany(row => row.Costs).Where(component => component.Key == key).Sum(component => component.Amount), 2);

        var healthLinked = Math.Round(
            expenses.Where(expense => healthLinkedExpenseIds.Contains(expense.Id)).Sum(expense => expense.Amount), 2);

        var reconciliation = new CostReconciliationDto
        {
            FarmExpensesTotal = Math.Round(expenses.Sum(expense => expense.Amount), 2),
            HealthLinkedExpenses = healthLinked,
            ExpensesAttributedToAnimals = SumComponents(CostComponentKeys.ExpenseDirect),
            ExpensesAllocatedFromLocations = SumComponents(CostComponentKeys.ExpenseLocation),
            ExpensesAllocatedFromFarmPool = SumComponents(CostComponentKeys.ExpenseFarm),
            ExpensesUnallocated = Math.Round(unallocated.ExpenseLocation + unallocated.ExpenseFarm, 2),

            FeedConsumedTotal = Math.Round(feedConsumedTotal, 2),
            FeedAttributedToAnimals = Math.Round(SumComponents(CostComponentKeys.FeedDirect), 2),
            FeedAllocatedFromLocations = SumComponents(CostComponentKeys.FeedLocation),
            FeedUnallocated = Math.Round(unallocated.FeedLocation, 2),

            HealthRecordsTotal = Math.Round(healthRecordsTotal, 2),
            HealthAttributedToAnimals = SumComponents(CostComponentKeys.HealthRecords),
            HealthCostOutsideTheExpenseLedger = Math.Round(healthRecordsTotal - healthLinked, 2),

            LabourTotal = Math.Round(labour, 2),

            // The two costs this report carries that no expense row does. Stated so the report
            // explains why its total is deliberately larger than the profit-and-loss expense line.
            CostOutsideTheExpenseLedger = Math.Round(feedConsumedTotal + labour, 2)
        };

        // Each of these is an identity by construction, which is the point: if a future change
        // breaks one, the report says so instead of showing a total that quietly lost money.
        reconciliation.ExpensesReconcile =
            reconciliation.HealthLinkedExpenses
            + reconciliation.ExpensesAttributedToAnimals
            + reconciliation.ExpensesAllocatedFromLocations
            + reconciliation.ExpensesAllocatedFromFarmPool
            + reconciliation.ExpensesUnallocated
            == reconciliation.FarmExpensesTotal;

        reconciliation.FeedReconciles =
            reconciliation.FeedAttributedToAnimals
            + reconciliation.FeedAllocatedFromLocations
            + reconciliation.FeedUnallocated
            == reconciliation.FeedConsumedTotal;

        reconciliation.HealthReconciles =
            reconciliation.HealthAttributedToAnimals == reconciliation.HealthRecordsTotal;

        return reconciliation;
    }

    private static decimal UnassignedMoney(CostReconciliationDto reconciliation)
    {
        var unassigned = 0m;

        if (!reconciliation.ExpensesReconcile)
        {
            unassigned += reconciliation.FarmExpensesTotal
                - (reconciliation.HealthLinkedExpenses + reconciliation.ExpensesAttributedToAnimals
                    + reconciliation.ExpensesAllocatedFromLocations + reconciliation.ExpensesAllocatedFromFarmPool
                    + reconciliation.ExpensesUnallocated);
        }

        if (!reconciliation.FeedReconciles)
        {
            unassigned += reconciliation.FeedConsumedTotal
                - (reconciliation.FeedAttributedToAnimals + reconciliation.FeedAllocatedFromLocations
                    + reconciliation.FeedUnallocated);
        }

        if (!reconciliation.HealthReconciles)
            unassigned += reconciliation.HealthRecordsTotal - reconciliation.HealthAttributedToAnimals;

        return Math.Round(unassigned, 2);
    }

    private static List<HerdCostRowDto> BuildHerds(List<AnimalCostRowDto> animals) =>
        animals
            .GroupBy(animal => (animal.LocationId, animal.LocationName))
            .Select(group => new HerdCostRowDto
            {
                LocationId = group.Key.LocationId,
                LocationName = group.Key.LocationName ?? "Not in a location",
                AnimalCount = group.Count(),
                AnimalDays = group.Sum(animal => animal.AnimalDays),
                TotalCost = group.Sum(animal => animal.TotalCost),
                TotalRevenue = group.Sum(animal => animal.TotalRevenue),
                Margin = group.Sum(animal => animal.Margin)
            })
            .OrderByDescending(herd => herd.TotalCost)
            .ToList();

    // ── loading ─────────────────────────────────────────────

    private async Task<List<AnimalFact>> LoadAnimalsAsync(Guid farmId) =>
        await _context.Animals
            .Where(animal => animal.FarmId == farmId)
            .Select(animal => new AnimalFact(
                animal.Id,
                animal.TagNumber,
                animal.Name,
                animal.LocationId,
                animal.Location != null ? animal.Location.Name : null,
                animal.AcquisitionDate,
                animal.DateOfBirth,
                animal.AnimalStatus!.Category,
                animal.DeletedAt))
            .ToListAsync();

    private async Task<HashSet<Guid>> LoadHealthLinkedExpenseIdsAsync(Guid farmId, DateTime from, DateTime toExclusive)
    {
        var medical = await _context.MedicalRecords
            .Where(record => record.FarmId == farmId && !record.IsDeleted && record.ExpenseId != null
                && record.DateRecorded >= from && record.DateRecorded < toExclusive)
            .Select(record => record.ExpenseId!.Value)
            .ToListAsync();

        var vaccination = await _context.VaccinationRecords
            .Where(record => record.FarmId == farmId && record.ExpenseId != null
                && record.DateGiven >= from && record.DateGiven < toExclusive)
            .Select(record => record.ExpenseId!.Value)
            .ToListAsync();

        return medical.Concat(vaccination).ToHashSet();
    }

    // ── private shapes ──────────────────────────────────────

    /// <summary>
    /// <paramref name="To"/> is what the row shows (null means still present);
    /// <paramref name="LastDay"/> is the last day of presence clamped to the range, which is
    /// what the location timeline measures to.
    /// </summary>
    private sealed record Presence(
        DateTime From, DateTime? To, DateTime LastDay, int Days, bool StartIsUnknown, bool DepartureIsUnknown);

    private sealed record StatusFact(Guid Id, string Name, AnimalStatusCategory Category);

    private sealed record StatusEventFact(Guid AnimalId, DateTime OccurredAt, string Title, Guid? RelatedEntityId);

    private sealed record TransferFact(Guid AnimalId, Guid? FromLocationId, Guid ToLocationId, DateTime TransferredAt);

    private sealed record ExpenseFact(Guid Id, Guid? AnimalId, Guid? LocationId, decimal Amount);

    private sealed record IncomeFact(Guid? AnimalId, Guid? LocationId, decimal Amount);

    private sealed record CountFact(Guid Key, int Count);

    private sealed record AnimalFact(
        Guid Id,
        string TagNumber,
        string? Name,
        Guid? LocationId,
        string? LocationName,
        DateTime? AcquisitionDate,
        DateTime? DateOfBirth,
        AnimalStatusCategory StatusCategory,
        DateTime? DeletedAt);

    /// <summary>What each pool could not spread, kept per source so the expense identity can be checked.</summary>
    private sealed class UnallocatedBuckets
    {
        public decimal FeedLocation { get; set; }
        public decimal ExpenseLocation { get; set; }
        public decimal ExpenseFarm { get; set; }
        public decimal Labour { get; set; }
        public decimal IncomeLocation { get; set; }
        public decimal IncomeFarm { get; set; }

        public decimal Total => FeedLocation + ExpenseLocation + ExpenseFarm + Labour + IncomeLocation + IncomeFarm;
    }

    /// <summary>An animal mid-assembly, before the pools have been spread and the DTO is built.</summary>
    private sealed class RowBuilder
    {
        public required AnimalFact Animal { get; init; }
        public required DateTime PresenceFrom { get; init; }
        public required DateTime? PresenceTo { get; init; }
        public required int Days { get; init; }
        public required Dictionary<Guid, int> LocationDays { get; init; }
        public required decimal FeedCost { get; init; }
        public required int FeedRecords { get; init; }
        public required decimal HealthCost { get; init; }
        public required int HealthRecords { get; init; }
        public required decimal ExpenseCost { get; init; }
        public required int ExpenseRecords { get; init; }
        public required decimal Income { get; init; }
        public required int IncomeRecords { get; init; }
        public required bool StartIsUnknown { get; init; }
        public required bool DepartureIsUnknown { get; init; }

        private readonly List<CostComponentDto> _costs = new();
        private readonly List<CostComponentDto> _revenue = new();

        public bool AddFeedLocation(int days, int poolDays, decimal pool, decimal amount, decimal rounding) =>
            AddAllocated(_costs, CostComponentKeys.FeedLocation, "Feed (shared pen)",
                "Group feed for a pen this animal was in", CostAllocationMethods.LocationAnimalDays,
                days, poolDays, pool, amount, rounding);

        public bool AddExpenseLocation(int days, int poolDays, decimal pool, decimal amount, decimal rounding) =>
            AddAllocated(_costs, CostComponentKeys.ExpenseLocation, "Expenses for its pen",
                "Expense records against a location this animal was in", CostAllocationMethods.LocationAnimalDays,
                days, poolDays, pool, amount, rounding);

        public bool AddIncomeLocation(int days, int poolDays, decimal pool, decimal amount, decimal rounding) =>
            AddAllocated(_revenue, CostComponentKeys.IncomeLocation, "Income for its pen",
                "Income records against a location this animal was in", CostAllocationMethods.LocationAnimalDays,
                days, poolDays, pool, amount, rounding);

        public bool AddLabour(int days, int poolDays, decimal pool, decimal amount, decimal rounding) =>
            AddAllocated(_costs, CostComponentKeys.LabourFarm, "Labour (shared)",
                "Salary payments for the farm", CostAllocationMethods.FarmAnimalDays,
                days, poolDays, pool, amount, rounding);

        public bool AddExpenseFarm(int days, int poolDays, decimal pool, decimal amount, decimal rounding) =>
            AddAllocated(_costs, CostComponentKeys.ExpenseFarm, "Farm-wide expenses (shared)",
                "Expense records with no animal or location", CostAllocationMethods.FarmAnimalDays,
                days, poolDays, pool, amount, rounding);

        public bool AddIncomeFarm(int days, int poolDays, decimal pool, decimal amount, decimal rounding) =>
            AddAllocated(_revenue, CostComponentKeys.IncomeFarm, "Farm-wide income (shared)",
                "Income records with no animal or location", CostAllocationMethods.FarmAnimalDays,
                days, poolDays, pool, amount, rounding);

        private static bool AddAllocated(
            List<CostComponentDto> target,
            string key,
            string label,
            string source,
            string method,
            int days,
            int poolDays,
            decimal pool,
            decimal amount,
            decimal rounding)
        {
            // A pool smaller than a cent cannot reach a row: the amount would round to zero
            // and the row would claim a component it does not have.
            if (amount == 0m && rounding == 0m)
                return false;

            target.Add(new CostComponentDto
            {
                Key = key,
                Label = label,
                Source = source,
                Method = method,
                Amount = amount,
                PoolAmount = Math.Round(pool, 2),
                AllocatedDays = days,
                PoolDays = poolDays,
                RoundingAdjustment = rounding
            });

            return true;
        }

        public AnimalCostRowDto ToDto(int totalAnimalDays)
        {
            if (FeedCost > 0m)
            {
                _costs.Insert(0, new CostComponentDto
                {
                    Key = CostComponentKeys.FeedDirect,
                    Label = "Feed (this animal)",
                    Source = "Feed records naming this animal",
                    Method = CostAllocationMethods.Direct,
                    Amount = Math.Round(FeedCost, 2),
                    RecordCount = FeedRecords
                });
            }

            if (HealthCost > 0m)
            {
                _costs.Add(new CostComponentDto
                {
                    Key = CostComponentKeys.HealthRecords,
                    Label = "Health (medical and vaccinations)",
                    Source = "Medical and vaccination records for this animal",
                    Method = CostAllocationMethods.Direct,
                    Amount = Math.Round(HealthCost, 2),
                    RecordCount = HealthRecords
                });
            }

            if (ExpenseCost > 0m)
            {
                _costs.Add(new CostComponentDto
                {
                    Key = CostComponentKeys.ExpenseDirect,
                    Label = "Expenses against this animal",
                    Source = "Expense records naming this animal",
                    Method = CostAllocationMethods.Direct,
                    Amount = Math.Round(ExpenseCost, 2),
                    RecordCount = ExpenseRecords
                });
            }

            if (Income > 0m)
            {
                _revenue.Insert(0, new CostComponentDto
                {
                    Key = CostComponentKeys.IncomeDirect,
                    Label = "Income against this animal",
                    Source = "Income records naming this animal",
                    Method = CostAllocationMethods.Direct,
                    Amount = Math.Round(Income, 2),
                    RecordCount = IncomeRecords
                });
            }

            var costs = _costs.ToList();
            var revenue = _revenue.ToList();
            var totalCost = costs.Sum(component => component.Amount);
            var totalRevenue = revenue.Sum(component => component.Amount);

            var warnings = new List<string>();
            if (StartIsUnknown)
                warnings.Add(CostWarningCodes.PresenceStartUnknown);

            if (DepartureIsUnknown)
                warnings.Add(CostWarningCodes.DepartureUnknown);

            return new AnimalCostRowDto
            {
                AnimalId = Animal.Id,
                TagNumber = Animal.TagNumber,
                Name = Animal.Name,
                LocationId = Animal.LocationId,
                LocationName = Animal.LocationName,
                PresentFrom = PresenceFrom,
                PresentTo = PresenceTo,
                AnimalDays = Days,
                ShareOfFarmDays = totalAnimalDays > 0
                    ? Math.Round((decimal)Days / totalAnimalDays, 6, MidpointRounding.AwayFromZero)
                    : 0m,
                Costs = costs,
                Revenue = revenue,
                TotalCost = totalCost,
                TotalRevenue = totalRevenue,
                Margin = totalRevenue - totalCost,
                Warnings = warnings
            };
        }
    }
}
