namespace FMS.Application.Common;

public interface ICurrentUserService
{
    Guid? GetUserId();
}
