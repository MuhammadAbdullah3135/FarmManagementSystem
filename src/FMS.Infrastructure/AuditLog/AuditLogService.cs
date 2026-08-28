using FMS.Application.AuditLog;
using FMS.Application.Common;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.AuditLog;

public class AuditLogService : IAuditLogService
{
    private readonly FmsDbContext _context;

    public AuditLogService(FmsDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PagedResult<AuditLogDto>>> GetAuditLogsAsync(Guid farmId, AuditLogFilter filter)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 20 : Math.Min(filter.PageSize, 200);

        var query = _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.FarmId == farmId);

        if (filter.Action.HasValue)
            query = query.Where(a => a.Action == filter.Action.Value);

        if (filter.UserId.HasValue)
            query = query.Where(a => a.UserId == filter.UserId.Value);

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
            query = query.Where(a => a.EntityType.Contains(filter.EntityType));

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
            query = query.Where(a => a.EntityId == filter.EntityId);

        if (filter.FromDate.HasValue)
            query = query.Where(a => a.Timestamp >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(a => a.Timestamp <= filter.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(a =>
                a.EntityType.Contains(term) ||
                (a.UserEmail != null && a.UserEmail.Contains(term)) ||
                a.EntityId.Contains(term));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                FarmId = a.FarmId,
                UserEmail = a.UserEmail,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                Action = a.Action,
                OldValues = a.OldValues,
                NewValues = a.NewValues,
                Timestamp = a.Timestamp,
                IpAddress = a.IpAddress
            })
            .ToListAsync();

        return Result<PagedResult<AuditLogDto>>.Success(new PagedResult<AuditLogDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }
}
