namespace FMS.Domain.Tests;

/// <summary>
/// Uploads used to be published as static files under a public <c>/uploads</c> path that
/// ran after the authorization middleware — so every animal image and document was
/// readable anonymously, and farm scoping never applied to it.
///
/// This is a tripwire: the request pipeline is configured in source, so the cheapest
/// reliable way to stop the hole silently reappearing is to assert the wiring is gone.
/// </summary>
public class UploadStaticServingTests
{
    private static string ProgramSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FarmManagementSystem.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        var programPath = Path.Combine(directory!.FullName, "src", "FMS.API", "Program.cs");
        Assert.True(File.Exists(programPath), $"Expected to find Program.cs at {programPath}");

        return File.ReadAllText(programPath);
    }

    [Fact]
    public void Uploads_AreNotServedAsPublicStaticFiles()
    {
        var source = ProgramSource();

        Assert.DoesNotContain("UseStaticFiles", source);
        Assert.DoesNotContain("\"/uploads\"", source);
    }

    [Fact]
    public void StoredFiles_AreOnlyReachableThroughAuthorizedEndpoints()
    {
        var source = ProgramSource();

        // Public URLs of the form /uploads/{key} must not be constructed anywhere.
        Assert.DoesNotContain("$\"/uploads/", source);
    }
}
