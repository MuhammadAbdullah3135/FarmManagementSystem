namespace FMS.Mobile.Services;

public interface IAuthService
{
    Task<bool> LoginAsync(string email, string password);
    Task LogoutAsync();
    Task<bool> RestoreSessionAsync();
    Task<List<FarmDto>> GetUserFarmsAsync();
    Task SelectFarmAsync(Guid farmId);
    Guid? ActiveFarmId { get; }
    bool IsAuthenticated { get; }
}

public class FarmDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}
