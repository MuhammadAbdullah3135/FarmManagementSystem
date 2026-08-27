namespace FMS.Application.Breeding;

public class LineageNodeDto
{
    public Guid Id { get; set; }
    public string TagNumber { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Sex { get; set; } = string.Empty;
    public string? Breed { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public bool IsRoot { get; set; }
    public LineageNodeDto? Sire { get; set; }
    public LineageNodeDto? Dam { get; set; }
    public List<LineageNodeDto> Offspring { get; set; } = new();
}

public class LineageResponse
{
    public LineageNodeDto Root { get; set; } = null!;
    public int AncestorDepth { get; set; }
    public int DescendantDepth { get; set; }
}

public class LineageQueryParams
{
    public int AncestorDepth { get; set; } = 5;
    public int DescendantDepth { get; set; } = 3;
}
