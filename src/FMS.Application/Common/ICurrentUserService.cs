namespace FMS.Application.Common;

public interface ICurrentUserService
{
    Guid? GetUserId();

    /// <summary>
    /// The signed-in user's email, when the request carries one. Recorded on salary
    /// payments at write time, so the ledger keeps answering "who paid?" even if the
    /// account is later removed.
    /// </summary>
    string? GetUserEmail();
}
