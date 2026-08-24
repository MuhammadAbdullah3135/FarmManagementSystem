using FMS.Application.Common;
using FMS.Application.Performance;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Performance;

public class PerformanceService : IPerformanceService
{
    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public PerformanceService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<PerformanceReviewDto>> GetReviewByIdAsync(Guid farmId, Guid id)
    {
        var review = await QueryReviews(farmId)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (review == null)
            return Result<PerformanceReviewDto>.NotFound("Performance review not found");

        return Result<PerformanceReviewDto>.Success(MapReview(review));
    }

    public async Task<Result<PagedResult<PerformanceReviewDto>>> GetReviewsAsync(Guid farmId, PerformanceReviewListFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 || filter.PageSize > 100 ? 20 : filter.PageSize;

        var query = QueryReviews(farmId);

        if (filter.EmployeeId.HasValue)
            query = query.Where(r => r.EmployeeId == filter.EmployeeId.Value);

        if (filter.MinRating.HasValue)
            query = query.Where(r => r.Rating >= filter.MinRating.Value);

        if (filter.MaxRating.HasValue)
            query = query.Where(r => r.Rating <= filter.MaxRating.Value);

        if (filter.From.HasValue)
            query = query.Where(r => r.ReviewDate >= filter.From.Value.Date);

        if (filter.To.HasValue)
            query = query.Where(r => r.ReviewDate <= filter.To.Value.Date);

        var totalCount = await query.CountAsync();

        var reviews = await query
            .OrderByDescending(r => r.ReviewDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Result<PagedResult<PerformanceReviewDto>>.Success(new PagedResult<PerformanceReviewDto>
        {
            Items = reviews.Select(MapReview).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<PerformanceReviewDto>> CreateReviewAsync(Guid farmId, CreatePerformanceReviewRequest request)
    {
        var validationError = Validate(request.Rating, request.ReviewDate, request.PeriodStart, request.PeriodEnd,
            request.Strengths, request.AreasForImprovement, request.Comments);
        if (validationError != null)
            return Result<PerformanceReviewDto>.Validation(validationError);

        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.FarmId == farmId);
        if (employee == null)
            return Result<PerformanceReviewDto>.NotFound("Employee not found");

        var now = DateTime.UtcNow;
        var review = new PerformanceReview
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            EmployeeId = request.EmployeeId,
            Rating = request.Rating,
            ReviewDate = request.ReviewDate.Date,
            PeriodStart = request.PeriodStart?.Date,
            PeriodEnd = request.PeriodEnd?.Date,
            Strengths = request.Strengths?.Trim(),
            AreasForImprovement = request.AreasForImprovement?.Trim(),
            Comments = request.Comments?.Trim(),
            ReviewedBy = _currentUser.GetUserId(),
            CreatedAt = now,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.PerformanceReviews.Add(review);
        await _context.SaveChangesAsync();

        return Result<PerformanceReviewDto>.Success(MapReview(review));
    }

    public async Task<Result<PerformanceReviewDto>> UpdateReviewAsync(Guid farmId, Guid id, UpdatePerformanceReviewRequest request)
    {
        var review = await _context.PerformanceReviews
            .FirstOrDefaultAsync(r => r.Id == id && r.FarmId == farmId);
        if (review == null)
            return Result<PerformanceReviewDto>.NotFound("Performance review not found");

        var validationError = Validate(request.Rating, request.ReviewDate, request.PeriodStart, request.PeriodEnd,
            request.Strengths, request.AreasForImprovement, request.Comments);
        if (validationError != null)
            return Result<PerformanceReviewDto>.Validation(validationError);

        review.Rating = request.Rating;
        review.ReviewDate = request.ReviewDate.Date;
        review.PeriodStart = request.PeriodStart?.Date;
        review.PeriodEnd = request.PeriodEnd?.Date;
        review.Strengths = request.Strengths?.Trim();
        review.AreasForImprovement = request.AreasForImprovement?.Trim();
        review.Comments = request.Comments?.Trim();
        review.ModifiedAt = DateTime.UtcNow;
        review.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();

        return Result<PerformanceReviewDto>.Success(MapReview(await LoadFullAsync(farmId, review.Id)));
    }

    public async Task<Result> DeleteReviewAsync(Guid farmId, Guid id)
    {
        var review = await _context.PerformanceReviews
            .FirstOrDefaultAsync(r => r.Id == id && r.FarmId == farmId);
        if (review == null)
            return Result.NotFound("Performance review not found");

        _context.PerformanceReviews.Remove(review);
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    // Helpers

    private IQueryable<PerformanceReview> QueryReviews(Guid farmId) =>
        _context.PerformanceReviews
            .AsNoTracking()
            .Include(r => r.Employee)
            .Where(r => r.FarmId == farmId);

    private async Task<PerformanceReview> LoadFullAsync(Guid farmId, Guid id) =>
        await QueryReviews(farmId).FirstAsync(r => r.Id == id);

    private static string? Validate(int rating, DateTime reviewDate, DateTime? periodStart, DateTime? periodEnd,
        string? strengths, string? areasForImprovement, string? comments)
    {
        if (rating is < 1 or > 5)
            return "Rating must be between 1 and 5";
        if (reviewDate.Date > DateTime.UtcNow.Date)
            return "Review date cannot be in the future";
        if (periodStart.HasValue && periodEnd.HasValue && periodEnd.Value < periodStart.Value)
            return "Period end cannot be before period start";
        if (!string.IsNullOrWhiteSpace(strengths) && strengths.Length > 2000)
            return "Strengths cannot exceed 2000 characters";
        if (!string.IsNullOrWhiteSpace(areasForImprovement) && areasForImprovement.Length > 2000)
            return "Areas for improvement cannot exceed 2000 characters";
        if (!string.IsNullOrWhiteSpace(comments) && comments.Length > 2000)
            return "Comments cannot exceed 2000 characters";
        return null;
    }

    private static PerformanceReviewDto MapReview(PerformanceReview review) => new()
    {
        Id = review.Id,
        EmployeeId = review.EmployeeId,
        EmployeeName = $"{review.Employee.FirstName} {review.Employee.LastName}",
        Rating = review.Rating,
        ReviewDate = review.ReviewDate,
        PeriodStart = review.PeriodStart,
        PeriodEnd = review.PeriodEnd,
        Strengths = review.Strengths,
        AreasForImprovement = review.AreasForImprovement,
        Comments = review.Comments,
        ReviewedBy = review.ReviewedBy,
        CreatedAt = review.CreatedAt
    };
}
