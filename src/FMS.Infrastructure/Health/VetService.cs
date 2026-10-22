using FMS.Application.Common;
using FMS.Application.Health;
using FMS.Domain.Entities;
using FMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FMS.Infrastructure.Health;

public class VetService : IVetService
{
    public const int NameMaxLength = 200;
    public const int PhoneMaxLength = 50;
    public const int EmailMaxLength = 200;
    public const int LicenseNumberMaxLength = 100;
    public const int NotesMaxLength = 2000;

    private readonly FmsDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public VetService(FmsDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<List<VetDto>>> GetVetsAsync(Guid farmId, bool includeInactive)
    {
        var query = _context.Vets.AsNoTracking().Where(v => v.FarmId == farmId);
        if (!includeInactive)
            query = query.Where(v => v.IsActive);

        var vets = await query
            .OrderBy(v => v.Name)
            .Select(v => new VetDto
            {
                Id = v.Id,
                FarmId = v.FarmId,
                Name = v.Name,
                Phone = v.Phone,
                Email = v.Email,
                LicenseNumber = v.LicenseNumber,
                IsActive = v.IsActive,
                Notes = v.Notes,
                CreatedAt = v.CreatedAt
            })
            .ToListAsync();

        return Result<List<VetDto>>.Success(vets);
    }

    public async Task<Result<VetDto>> GetVetByIdAsync(Guid farmId, Guid id)
    {
        var vet = await _context.Vets.AsNoTracking()
            .FirstOrDefaultAsync(v => v.FarmId == farmId && v.Id == id);
        return vet == null
            ? Result<VetDto>.NotFound("Vet not found", DomainMessageKeys.VetNotFound)
            : Result<VetDto>.Success(ToDto(vet));
    }

    public async Task<Result<VetDto>> CreateVetAsync(Guid farmId, CreateVetRequest request)
    {
        var error = Validate(
            request.Name, request.Phone, request.Email, request.LicenseNumber, request.Notes);
        if (error != null)
            return Result<VetDto>.Validation(error.Message, error.MessageKey, error.MessageArgs);

        var name = request.Name.Trim();
        if (await NameExistsAsync(farmId, name, null))
            return NameConflict(name);

        var licenseNumber = Clean(request.LicenseNumber);
        if (licenseNumber != null && await LicenseExistsAsync(farmId, licenseNumber, null))
            return LicenseConflict(licenseNumber);

        var vet = new Vet
        {
            Id = Guid.NewGuid(),
            FarmId = farmId,
            Name = name,
            Phone = Clean(request.Phone),
            Email = Clean(request.Email),
            LicenseNumber = licenseNumber,
            IsActive = true,
            Notes = Clean(request.Notes),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.GetUserId()
        };

        _context.Vets.Add(vet);
        await _context.SaveChangesAsync();
        return Result<VetDto>.Success(ToDto(vet));
    }

    public async Task<Result<VetDto>> UpdateVetAsync(Guid farmId, Guid id, UpdateVetRequest request)
    {
        var vet = await _context.Vets
            .FirstOrDefaultAsync(v => v.FarmId == farmId && v.Id == id);
        if (vet == null)
            return Result<VetDto>.NotFound("Vet not found", DomainMessageKeys.VetNotFound);

        var error = Validate(
            request.Name, request.Phone, request.Email, request.LicenseNumber, request.Notes);
        if (error != null)
            return Result<VetDto>.Validation(error.Message, error.MessageKey, error.MessageArgs);

        var name = request.Name.Trim();
        if (await NameExistsAsync(farmId, name, id))
            return NameConflict(name);

        var licenseNumber = Clean(request.LicenseNumber);
        if (licenseNumber != null && await LicenseExistsAsync(farmId, licenseNumber, id))
            return LicenseConflict(licenseNumber);

        // A rename deliberately does not rewrite VetName on historical clinical records.
        // The report keeps grouping by that retained string until a later phase.
        vet.Name = name;
        vet.Phone = Clean(request.Phone);
        vet.Email = Clean(request.Email);
        vet.LicenseNumber = licenseNumber;
        vet.IsActive = request.IsActive;
        vet.Notes = Clean(request.Notes);
        vet.ModifiedAt = DateTime.UtcNow;
        vet.ModifiedBy = _currentUser.GetUserId();

        await _context.SaveChangesAsync();
        return Result<VetDto>.Success(ToDto(vet));
    }

    public async Task<Result> DeleteVetAsync(Guid farmId, Guid id)
    {
        var vet = await _context.Vets
            .FirstOrDefaultAsync(v => v.FarmId == farmId && v.Id == id);
        if (vet == null)
            return Result.NotFound("Vet not found", DomainMessageKeys.VetNotFound);

        // Medical records are soft-deletable, but a soft-deleted row still carries the
        // foreign key and may be restored, so it blocks a delete just like an active one.
        var referenced =
            await _context.MedicalRecords.IgnoreQueryFilters().AnyAsync(m => m.VetId == id) ||
            await _context.VaccinationRecords.AnyAsync(v => v.VetId == id) ||
            await _context.BreedingRecords.AnyAsync(br => br.VetId == id) ||
            await _context.BirthRecords.AnyAsync(br => br.VetId == id);

        if (referenced)
            return Result.Conflict(
                "Cannot delete a vet that is referenced by clinical records; deactivate it instead",
                DomainMessageKeys.VetHasClinicalRecords);

        _context.Vets.Remove(vet);
        await _context.SaveChangesAsync();
        return Result.Success();
    }

    private Task<bool> NameExistsAsync(Guid farmId, string name, Guid? exceptId)
    {
        var normalized = name.ToLower();
        var query = _context.Vets.Where(v => v.FarmId == farmId);
        if (exceptId.HasValue)
            query = query.Where(v => v.Id != exceptId.Value);
        return query.AnyAsync(v => v.Name.ToLower() == normalized);
    }

    private Task<bool> LicenseExistsAsync(Guid farmId, string licenseNumber, Guid? exceptId)
    {
        var normalized = licenseNumber.ToLower();
        var query = _context.Vets.Where(v =>
            v.FarmId == farmId && v.LicenseNumber != null);
        if (exceptId.HasValue)
            query = query.Where(v => v.Id != exceptId.Value);
        return query.AnyAsync(v => v.LicenseNumber!.ToLower() == normalized);
    }

    private static Result<VetDto> NameConflict(string name) =>
        Result<VetDto>.Conflict(
            $"A vet named '{name}' already exists",
            DomainMessageKeys.VetNameExists,
            new Dictionary<string, object?> { ["name"] = name });

    private static Result<VetDto> LicenseConflict(string licenseNumber) =>
        Result<VetDto>.Conflict(
            $"A vet with licence number '{licenseNumber}' already exists",
            DomainMessageKeys.VetLicenseNumberExists,
            new Dictionary<string, object?> { ["licenseNumber"] = licenseNumber });

    private static Error? Validate(
        string? name,
        string? phone,
        string? email,
        string? licenseNumber,
        string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Error.Validation("Vet name is required", DomainMessageKeys.VetNameRequired);

        var trimmedName = name.Trim();
        if (trimmedName.Length > NameMaxLength)
            return Error.Validation(
                $"Vet name cannot exceed {NameMaxLength} characters",
                DomainMessageKeys.VetNameMaxLength,
                new Dictionary<string, object?> { ["max"] = NameMaxLength });

        if (phone is not null && phone.Trim().Length > PhoneMaxLength)
            return Error.Validation(
                $"Phone cannot exceed {PhoneMaxLength} characters",
                DomainMessageKeys.VetPhoneMaxLength,
                new Dictionary<string, object?> { ["max"] = PhoneMaxLength });

        if (email is not null && email.Trim().Length > EmailMaxLength)
            return Error.Validation(
                $"Email cannot exceed {EmailMaxLength} characters",
                DomainMessageKeys.VetEmailMaxLength,
                new Dictionary<string, object?> { ["max"] = EmailMaxLength });

        if (licenseNumber is not null && licenseNumber.Trim().Length > LicenseNumberMaxLength)
            return Error.Validation(
                $"Licence number cannot exceed {LicenseNumberMaxLength} characters",
                DomainMessageKeys.VetLicenseNumberMaxLength,
                new Dictionary<string, object?> { ["max"] = LicenseNumberMaxLength });

        if (notes is not null && notes.Trim().Length > NotesMaxLength)
            return Error.Validation(
                $"Notes cannot exceed {NotesMaxLength} characters",
                DomainMessageKeys.VetNotesMaxLength,
                new Dictionary<string, object?> { ["max"] = NotesMaxLength });

        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static VetDto ToDto(Vet vet) => new()
    {
        Id = vet.Id,
        FarmId = vet.FarmId,
        Name = vet.Name,
        Phone = vet.Phone,
        Email = vet.Email,
        LicenseNumber = vet.LicenseNumber,
        IsActive = vet.IsActive,
        Notes = vet.Notes,
        CreatedAt = vet.CreatedAt
    };
}
