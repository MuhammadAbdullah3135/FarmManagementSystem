using FMS.Application.Common;
using FMS.Application.Performance;
using FMS.Domain.Entities;
using FMS.Domain.Enums;
using FMS.Infrastructure.Persistence;
using FMS.Infrastructure.Performance;
using Microsoft.EntityFrameworkCore;

namespace FMS.Domain.Tests;

public class PerformanceTests
{
    private static FmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new FmsDbContext(options);
    }

    private static PerformanceService CreateService(FmsDbContext context) =>
        new(context, new FixedCurrentUser());

    private sealed class FixedCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid? GetUserId() => UserId;
    }

    private sealed record SeedData(Guid FarmId, Guid AliId, Guid BilalId);

    private static async Task<SeedData> SeedAsync(FmsDbContext context)
    {
        var farm = new Farm { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Farm" };
        var ali = new Employee
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            FirstName = "Ali",
            LastName = "Khan",
            SalaryType = SalaryType.Monthly,
            SalaryRate = 50000,
            HireDate = DateTime.UtcNow.Date.AddYears(-1),
            IsActive = true
        };
        var bilal = new Employee
        {
            Id = Guid.NewGuid(),
            FarmId = farm.Id,
            FirstName = "Bilal",
            LastName = "Ahmed",
            SalaryType = SalaryType.Daily,
            SalaryRate = 1500,
            HireDate = DateTime.UtcNow.Date.AddMonths(-2),
            IsActive = true
        };

        context.Farms.Add(farm);
        context.Employees.AddRange(ali, bilal);
        await context.SaveChangesAsync();

        return new SeedData(farm.Id, ali.Id, bilal.Id);
    }

    private static CreatePerformanceReviewRequest ValidRequest(SeedData seed, Action<CreatePerformanceReviewRequest>? mutate = null)
    {
        var request = new CreatePerformanceReviewRequest
        {
            EmployeeId = seed.AliId,
            Rating = 4,
            ReviewDate = DateTime.UtcNow.Date.AddDays(-7),
            PeriodStart = DateTime.UtcNow.Date.AddMonths(-6),
            PeriodEnd = DateTime.UtcNow.Date,
            Strengths = "Reliable and punctual",
            AreasForImprovement = "Record keeping",
            Comments = "Solid performer"
        };
        mutate?.Invoke(request);
        return request;
    }

    [Fact]
    public async Task CreateReview_Succeeds_AndSetsReviewer()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateReviewAsync(seed.FarmId, ValidRequest(seed));

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.Rating);
        Assert.Equal("Ali Khan", result.Value.EmployeeName);
        Assert.NotNull(result.Value.ReviewedBy); // reviewer captured from current user
    }

    [Fact]
    public async Task CreateReview_RatingBounds_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var zero = await service.CreateReviewAsync(seed.FarmId,
            ValidRequest(seed, r => r.Rating = 0));
        Assert.Equal("Validation", zero.Error!.Code);

        var six = await service.CreateReviewAsync(seed.FarmId,
            ValidRequest(seed, r => r.Rating = 6));
        Assert.Equal("Validation", six.Error!.Code);
    }

    [Fact]
    public async Task CreateReview_FutureDate_AndBadPeriod_ReturnsValidation()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var future = await service.CreateReviewAsync(seed.FarmId,
            ValidRequest(seed, r => r.ReviewDate = DateTime.UtcNow.Date.AddDays(2)));
        Assert.Equal("Validation", future.Error!.Code);

        var badPeriod = await service.CreateReviewAsync(seed.FarmId,
            ValidRequest(seed, r =>
            {
                r.PeriodStart = DateTime.UtcNow.Date;
                r.PeriodEnd = DateTime.UtcNow.Date.AddDays(-30);
            }));
        Assert.Equal("Validation", badPeriod.Error!.Code);
    }

    [Fact]
    public async Task CreateReview_UnknownEmployee_ReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var result = await service.CreateReviewAsync(seed.FarmId,
            ValidRequest(seed, r => r.EmployeeId = Guid.NewGuid()));
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task GetReviews_FiltersByEmployeeAndRatingRange()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        await service.CreateReviewAsync(seed.FarmId, ValidRequest(seed)); // Ali, rating 4
        await service.CreateReviewAsync(seed.FarmId, ValidRequest(seed, r =>
        {
            r.EmployeeId = seed.BilalId;
            r.Rating = 2;
        }));

        var byEmployee = await service.GetReviewsAsync(seed.FarmId, new PerformanceReviewListFilter
        {
            EmployeeId = seed.BilalId
        });
        var bilalReview = Assert.Single(byEmployee.Value!.Items);
        Assert.Equal("Bilal Ahmed", bilalReview.EmployeeName);

        var highRatings = await service.GetReviewsAsync(seed.FarmId, new PerformanceReviewListFilter
        {
            MinRating = 3,
            MaxRating = 5
        });
        Assert.Single(highRatings.Value!.Items);
        Assert.Equal(4, highRatings.Value.Items[0].Rating);
    }

    [Fact]
    public async Task UpdateReview_Succeeds_AndUnknownReturnsNotFound()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateReviewAsync(seed.FarmId, ValidRequest(seed));

        var updated = await service.UpdateReviewAsync(seed.FarmId, created.Value!.Id, new UpdatePerformanceReviewRequest
        {
            Rating = 5,
            ReviewDate = created.Value.ReviewDate,
            Comments = "Exceeded expectations after follow-up"
        });
        Assert.True(updated.IsSuccess);
        Assert.Equal(5, updated.Value!.Rating);
        Assert.Equal("Exceeded expectations after follow-up", updated.Value.Comments);

        var unknown = await service.UpdateReviewAsync(seed.FarmId, Guid.NewGuid(), new UpdatePerformanceReviewRequest
        {
            Rating = 3,
            ReviewDate = DateTime.UtcNow.Date
        });
        Assert.Equal("NotFound", unknown.Error!.Code);
    }

    [Fact]
    public async Task DeleteReview_Succeeds_AndRemovesFromList()
    {
        using var context = CreateContext();
        var seed = await SeedAsync(context);
        var service = CreateService(context);

        var created = await service.CreateReviewAsync(seed.FarmId, ValidRequest(seed));

        var deleted = await service.DeleteReviewAsync(seed.FarmId, created.Value!.Id);
        Assert.True(deleted.IsSuccess);

        var list = await service.GetReviewsAsync(seed.FarmId, new PerformanceReviewListFilter());
        Assert.Empty(list.Value!.Items);
    }
}
