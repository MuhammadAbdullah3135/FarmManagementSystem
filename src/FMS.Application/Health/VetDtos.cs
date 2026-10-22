namespace FMS.Application.Health;

public class VetDto
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LicenseNumber { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateVetRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LicenseNumber { get; set; }
    public string? Notes { get; set; }
}

public class UpdateVetRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LicenseNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}
