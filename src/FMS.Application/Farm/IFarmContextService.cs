namespace FMS.Application.Farm;

public interface IFarmContextService
{
    Guid? GetCurrentFarmId();
    void SetCurrentFarmId(Guid farmId);
}

public class FarmContext : IFarmContextService
{
    private Guid? _farmId;

    public Guid? GetCurrentFarmId() => _farmId;

    public void SetCurrentFarmId(Guid farmId) => _farmId = farmId;
}
