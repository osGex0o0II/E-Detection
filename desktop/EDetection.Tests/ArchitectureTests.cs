using System.Reflection;
using EDetection.Desktop.Services;
using EDetection.Domain;
using EDetection.Infrastructure;

namespace EDetection.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_DoesNotReferenceWinUI()
    {
        AssertNoWinUI(typeof(DomainAssemblyMarker).Assembly);
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceWinUI()
    {
        AssertNoWinUI(typeof(InfrastructureAssemblyMarker).Assembly);
    }

    [Fact]
    public void Application_DoesNotReferenceWinUI()
    {
        AssertNoWinUI(typeof(SettingsService).Assembly);
    }

    private static void AssertNoWinUI(Assembly assembly)
    {
        var references = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain("Microsoft.WinUI", references);
        Assert.DoesNotContain("Microsoft.WindowsAppSDK", references);
    }
}
