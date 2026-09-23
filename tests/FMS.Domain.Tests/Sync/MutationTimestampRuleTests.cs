using FMS.API.Validation.Animals;
using FMS.Application.Animal;
using FMS.Infrastructure.Common;

namespace FMS.Domain.Tests.Sync;

/// <summary>
/// The weight message and tolerance now have two homes: the FluentValidation validator that runs
/// on the single-record endpoint, and the inline check the service applies (which is the one the
/// sync path reaches, since queued items do not pass through model validation). They are asserted
/// equal here rather than assumed — this is the same drift guard 3.4/4.3 built for the import
/// rules, applied to the one rule phase 5.3 moved into a shared constant.
///
/// <para>
/// (The 4.5 investigation recorded that weights, attendance and tasks had "no validators at all".
/// That is wrong for weights: <see cref="CreateWeightRecordRequestValidator"/> exists and mirrors
/// the service's two timestamp/weight rules word for word. The other two workflows genuinely have
/// none.)
/// </para>
/// </summary>
public class MutationTimestampRuleTests
{
    [Fact]
    public void WeightValidator_AndTheSharedRule_AgreeOnTheMessage()
    {
        var validator = new CreateWeightRecordRequestValidator();

        var result = validator.Validate(new CreateWeightRecordRequest
        {
            WeightKg = 100m,
            RecordedAt = DateTime.UtcNow.AddDays(1)
        });

        var error = Assert.Single(result.Errors);
        Assert.Equal(MutationTimestampRules.WeightRecordedAtMessage, error.ErrorMessage);
    }

    [Fact]
    public void WeightValidator_AndTheSharedRule_AgreeAtTheToleranceBoundary()
    {
        var validator = new CreateWeightRecordRequestValidator();
        var now = DateTime.UtcNow;

        // Just inside the allowance: both accept it. Just outside: both refuse it. Asserting the
        // boundary, not only the message, is what catches the two constants drifting apart.
        var inside = now.Add(MutationTimestampRules.MaxFutureSkew) - TimeSpan.FromMinutes(1);
        var outside = now.Add(MutationTimestampRules.MaxFutureSkew) + TimeSpan.FromMinutes(1);

        Assert.False(MutationTimestampRules.IsTooFarInTheFuture(inside, now));
        Assert.True(validator.Validate(new CreateWeightRecordRequest { WeightKg = 100m, RecordedAt = inside }).IsValid);

        Assert.True(MutationTimestampRules.IsTooFarInTheFuture(outside, now));
        Assert.False(validator.Validate(new CreateWeightRecordRequest { WeightKg = 100m, RecordedAt = outside }).IsValid);
    }

    [Fact]
    public void TheDefaultWeightRequest_CarriesNoDeviceTime()
    {
        // A live request (default RecordedAt, the default DateTime) is what the service treats as
        // "stamp the server clock", so the rule must not refuse it.
        var validator = new CreateWeightRecordRequestValidator();

        Assert.True(validator.Validate(new CreateWeightRecordRequest { WeightKg = 100m }).IsValid);
    }
}
