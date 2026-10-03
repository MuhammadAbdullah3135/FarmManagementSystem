using System.Text.RegularExpressions;

namespace FMS.Domain.Tests.I18n;

/// <summary>
/// Stops the validation family from growing back into English.
/// <para>
/// This is the last family. The "not found" sentences were keyed and gated by
/// <see cref="LookupMessageKeyGuardTests"/>, and the conflict and supersede ones by
/// <see cref="ConflictMessageKeyGuardTests"/>; until this test existed, nothing matched a plain
/// keyless <c>Validation</c> call at all. That is the largest family in the domain — 104 sites
/// when it was written — and it was the one with no guard, which is the same order of mistake
/// as guarding the families that were already finished.
/// </para>
/// <para>
/// The rule is narrow in the same way and for the same reason: a key is required on a
/// <c>Validation</c> refusal whose message is a string literal, plain or interpolated, and a
/// message held in a variable is left alone because such a call carries its key in the variable
/// too. The interpolated form is included rather than deferred, because unlike the conflict
/// family there is nothing here that <em>cannot</em> be keyed yet — an argument dictionary is
/// always somewhere to put the values — so excluding it would only be a way of not noticing.
/// </para>
/// <para>
/// The pattern is duplicated from <see cref="ConflictMessageKeyGuardTests"/> rather than shared
/// with it, deliberately. The two guard different factories, and a helper over two regexes is a
/// place where both could be wrong together; the comments that matter are repeated instead, so
/// editing one guard does not read as editing the other.
/// </para>
/// </summary>
public class ValidationMessageKeyGuardTests
{
    /// <summary>
    /// A validation refusal that answers with a literal sentence and no key.
    /// <para>
    /// The closing paren follows immediately, which is exactly "no key was passed" — a keyed
    /// call cannot match, because the key follows a comma.
    /// </para>
    /// <para>
    /// <b>This must be matched against the whole file, not line by line.</b> Every keyed call
    /// in this project puts its message on the line after <c>.Validation(</c>, so a per-line
    /// match cannot see the two together and silently passes — which is exactly the bug the
    /// conflict guard had, found there by removing a key and watching it stay green.
    /// </para>
    /// <para>
    /// The reported line is where the match starts.
    /// </para>
    /// <para>
    /// A note on <c>RegexOptions.Singleline</c>, because the obvious explanation for it is
    /// the wrong one: it makes <c>.</c> match a newline, and this pattern has no <c>.</c> in
    /// it. What lets <c>\s*</c> cross the line break is that <c>\s</c> already matches a
    /// newline. The option is harmless and stays for symmetry with the other guards, but the
    /// thing that actually makes this guard work is scanning the file as one string — which is
    /// why the cross-check in <c>scripts/i18n-backlog-audit.mjs</c>, which ports this pattern
    /// to JavaScript, needs no equivalent flag to agree with it.
    /// </para>
    /// </summary>
    private static readonly Regex Keyless = new(
        @"\.(?:Validation|Failure)\s*\(\s*\$?""[^""]*""\s*\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Every keyless validation refusal in one file, as file-relative lines to report.
    /// <para>
    /// The line is where the match <em>starts</em>, and the text is the whole match with its
    /// newlines collapsed — so the report names the file, the line and the sentence that has to
    /// be keyed, which is the only thing anyone needs in order to fix it.
    /// </para>
    /// </summary>
    private static List<string> Offenders(string relative, string code)
    {
        var found = new List<string>();
        for (var match = Keyless.Match(code); match.Success; match = match.NextMatch())
        {
            var line = code.Take(match.Index).Count(ch => ch == '\n') + 1;
            var text = string.Join(' ', match.Value.Split('\n').Select(part => part.Trim()));
            found.Add($"{relative}:{line}  {text}");
        }

        return found;
    }

    /// <summary>
    /// The validation sites still to be keyed, listed by file.
    /// <para>
    /// Named rather than hidden behind a floor, because this is the one guard whose job is
    /// still in progress and a count alone would hide which sentences it is not yet watching.
    /// Each entry is removed as its service is swept, and the list going empty is the signal
    /// that this test can be deleted in favour of a bare assertion — the same shape the
    /// conflict guard's <c>AllowedKeyless</c> has, and for the same reason: an exemption with
    /// no visible end is a hole with a comment on it.
    /// </para>
    /// <para>
    /// <c>FeedService</c> and <c>AnimalService</c> have been swept and are no longer listed,
    /// which is what moved the audit from 122 unkeyed sites to 89. Leaving them on would have
    /// re-opened a hole in the guard over 33 sentences that are now keyed, and nothing else
    /// would have said so — the guard passes whether a file is listed or not.
    /// </para>
    /// <para>
    /// Two more were removed by the cross-check rather than by a sweep:
    /// <c>FarmService</c> and <c>FarmExportService</c> were listed because they emit unkeyed
    /// sentences, but those sentences are <c>Unauthorized</c> and <c>Unavailable</c>, which
    /// this guard does not claim — so it had never opened either file, and the exemption was
    /// decoration. That is the drift this list is exposed to, and
    /// <c>node scripts/i18n-backlog-audit.mjs --guard</c> now fails on it in CI.
    /// </para>
    /// <para>
    /// <c>MedicineService</c> and <c>VaccineService</c> were swept next: sixteen sites for ten
    /// new keys, because two of their sentences are ones the health services already shared
    /// with the feed ones — “Quantity must be greater than zero” is the third service to say
    /// it, and it reuses the key rather than becoming a third translation to keep in step.
    /// <c>EmployeeService</c> followed: seven sites, six new keys, and one reuse of
    /// <c>FromDateAfterToDate</c> from the feed sweep. Then <c>BreedingService</c> and
    /// <c>InventoryService</c>: twelve sites, ten new keys, and a third reuse, of
    /// <c>MovementDateFuture</c>. Three of the ten so far have been a sentence another service
    /// already said, which is the argument for sweeping by service rather than by key.
    /// </para>
    /// <para>
    /// The seven import services left together rather than one at a time. They were put on this
    /// list by this guard on its first run, having been missed by the audit's backlog entirely,
    /// and each said the identical sentence about an empty sheet — verified byte-identical at
    /// all seven before they shared one key. Seven entries became zero in one step, and the
    /// cross-check confirms none of them has a hole behind it.
    /// </para>
    /// </summary>
    private static readonly string[] StillToKey =
    {
        "src/FMS.Infrastructure/Farm/FarmMembershipService.cs",
        "src/FMS.Infrastructure/Auth/AuthService.cs",
        "src/FMS.Infrastructure/Attendance/AttendanceService.cs",
        "src/FMS.Infrastructure/Import/SpreadsheetReader.cs",
        "src/FMS.Infrastructure/Inventory/SupplierService.cs",
        "src/FMS.Infrastructure/Inventory/CustomerService.cs",
        "src/FMS.Infrastructure/Files/FileStorageService.cs",
        "src/FMS.Infrastructure/Files/S3FileStorageService.cs",
        "src/FMS.Infrastructure/Health/MedicalRecordService.cs",
        "src/FMS.Infrastructure/Health/WeightCheckScheduleService.cs",
        "src/FMS.Infrastructure/Tasks/FarmTaskService.cs",
        "src/FMS.Infrastructure/Files/FileUploadValidator.cs",
        "src/FMS.Infrastructure/Reports/CostAttributionService.cs",
        "src/FMS.Infrastructure/Notifications/PushSubscriptionService.cs",
    };

    [Fact]
    public void No_validation_refusal_is_added_outside_the_known_backlog()
    {
        // The guard's job while the backlog lasts: a *new* keyless validation sentence fails
        // here, while the sentences already known and listed below do not. It is a ratchet, not
        // a wall — which is the honest shape for the last family, since a wall would have had to
        // be 104 sentences wide on the day it was written.
        var offenders = new List<string>();
        foreach (var file in ServerSources.SourceFiles())
        {
            var relative = ServerSources.Relative(file);
            if (StillToKey.Contains(relative)) continue;
            offenders.AddRange(Offenders(relative, ServerSources.WithoutComments(File.ReadAllText(file))));
        }

        Assert.True(
            offenders.Count == 0,
            "These validation refusals carry no i18n key, so they reach a Spanish or Arabic "
            + "reader in English. Add the matching DomainMessageKeys constant as the second "
            + "argument (and the key to the client bundles):\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_known_backlog_is_only_shrinking()
    {
        // A file left on `StillToKey` after its sentences are keyed re-opens a hole in the guard
        // for no reason, and nothing else would say so — the guard above would pass either way.
        // This is the ceiling on the exemption: a file may leave the list, never join it.
        //
        // Staleness is judged per file, not in aggregate. The first version asked only whether
        // *any* listed file still had an offender, so a list of twenty-six was healthy on the
        // strength of one entry and its failure message named all twenty-six as needing
        // removal — which is advice that cannot be acted on.
        var stale = new List<string>();
        var remaining = 0;
        foreach (var file in ServerSources.SourceFiles())
        {
            var relative = ServerSources.Relative(file);
            if (!StillToKey.Contains(relative)) continue;
            var count = Offenders(relative, ServerSources.WithoutComments(File.ReadAllText(file))).Count;
            if (count == 0) stale.Add(relative);
            else remaining += count;
        }

        Assert.True(
            stale.Count == 0,
            "These files are on StillToKey but every validation refusal in them is keyed, so "
            + "the exemption buys nothing and the guard is not watching them at all. Remove them "
            + "from StillToKey:\n  " + string.Join("\n  ", stale)
            + $"\n({remaining} offenders remain across the files that legitimately still need keying.)");
    }

    [Fact]
    public void The_guard_itself_would_notice_a_keyless_validation_refusal()
    {
        // A guard that cannot fail is worse than none, because it reads as coverage. Every shape
        // it must catch and every shape it must leave alone is exercised here.

        // Caught: the plain form, in both factories the domain uses.
        Assert.True(Keyless.IsMatch("return Error.Validation(\"First name is required\");"));
        Assert.True(Keyless.IsMatch("return Result<DepartmentDto>.Validation(\"Name is required\");"));
        Assert.True(Keyless.IsMatch("return Result.Validation(\"Quantity must be greater than zero\");"));

        // Caught: an interpolated sentence, which is the one a reader is most likely to meet in
        // the wrong language because it names a value of theirs.
        Assert.True(Keyless.IsMatch("return Error.Validation($\"Column {column + 1} does not exist\");"));

        // Caught across a line break, which is how every keyed call in this project is written
        // and therefore how a new one will arrive. A per-line scan cannot see this.
        Assert.True(Keyless.IsMatch(
            "return Result<FeedRecordDto>.Validation(\n"
            + "    \"Quantity must be greater than zero\");"));
        Assert.Equal(
            "f.cs:1  .Validation( \"Quantity must be greater than zero\")",
            Offenders("f.cs", "return Result<FeedRecordDto>.Validation(\n"
                + "    \"Quantity must be greater than zero\");").Single());

        // Left alone: already keyed, because the key follows the comma. This is the form every
        // keyed validation call in this project takes.
        Assert.False(Keyless.IsMatch(
            "return Error.Validation(\"First name is required\", DomainMessageKeys.FirstNameRequired);"));
        Assert.False(Keyless.IsMatch(
            "return Result<MedicineUsageDto>.Validation(\n"
            + "    $\"Insufficient stock. Available: {totalAvailable}\",\n"
            + "    DomainMessageKeys.MedicineStockInsufficient,\n"
            + "    new Dictionary<string, object?> { [\"available\"] = totalAvailable });"));

        // Left alone: a message in a variable, which carries its key in the variable.
        Assert.False(Keyless.IsMatch("return Error.Validation(rule.ValidationMessage);"));

        // Not this family's factory at all. `LookupMessageKeyGuardTests` covers "not found"
        // and `ConflictMessageKeyGuardTests` covers conflict and supersede; this one names
        // only the two it is responsible for, so a sentence that family already watches is not
        // reported twice.
        Assert.False(Keyless.IsMatch("return Error.NotFound(\"Animal not found\");"));
        Assert.False(Keyless.IsMatch("return Error.Conflict(\"Already exists\");"));
        Assert.False(Keyless.IsMatch("return Error.Superseded(\"Already applied\");"));
        Assert.False(Keyless.IsMatch("return Error.Unexpected(\"Failed to store file\");"));

        // A commented-out example is prose, not a call site.
        Assert.False(Keyless.IsMatch(ServerSources.WithoutComments(
            "// return Error.Validation(\"First name is required\");")));
    }
}
