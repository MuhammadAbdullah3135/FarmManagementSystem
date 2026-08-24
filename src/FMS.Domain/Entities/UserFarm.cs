using FMS.Domain.Common;

namespace FMS.Domain.Entities;

public class UserFarm : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid FarmId { get; set; }
    public string Role { get; set; } = "Viewer";

    public User User { get; set; } = null!;
    public Farm Farm { get; set; } = null!;
}
