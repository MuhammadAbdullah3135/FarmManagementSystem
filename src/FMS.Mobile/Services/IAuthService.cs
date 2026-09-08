namespace FMS.Mobile.Services;

public interface IAuthService
{
    Task<bool> LoginAsync(string email, string password);
    Task<bool> RegisterAsync(string email, string password, string firstName, string lastName, string accountName);
    Task LogoutAsync();
    Task<bool> RestoreSessionAsync();
    Task<List<FarmDto>> GetUserFarmsAsync();
    Task<(List<FarmDto> farms, string? diagnostic)> GetUserFarmsAsyncWithDiagnostics();
    Task SelectFarmAsync(Guid farmId);
    Guid? ActiveFarmId { get; }
    bool IsAuthenticated { get; }
    string? LastErrorMessage { get; }
}

public class FarmDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}
