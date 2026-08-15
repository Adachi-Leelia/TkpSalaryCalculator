using System.Xml.Linq;
using System.Text.RegularExpressions;
using TkpSalaryCalculator.Application.UseCases;

namespace TkpSalaryCalculator.Application.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ApplicationProject_ReferencesOnlyDomainProjectAndNoPlatformPackages()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src", "TkpSalaryCalculator.Application", "TkpSalaryCalculator.Application.csproj"));
        var references = project.Descendants().Where(x => x.Name.LocalName == "ProjectReference")
            .Select(x => (string?)x.Attribute("Include")).ToArray();
        var packages = project.Descendants().Where(x => x.Name.LocalName == "PackageReference")
            .Select(x => (string?)x.Attribute("Include")).ToArray();
        var frameworkReferences = project.Descendants().Where(x => x.Name.LocalName == "FrameworkReference").ToArray();
        var assemblyReferences = project.Descendants().Where(x => x.Name.LocalName == "Reference").ToArray();

        Assert.Single(references);
        Assert.Contains("TkpSalaryCalculator.Domain", references[0], StringComparison.Ordinal);
        Assert.Empty(packages);
        Assert.Empty(frameworkReferences);
        Assert.Empty(assemblyReferences);
        Assert.DoesNotContain(references, x => x?.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ApplicationAssemblyAndSource_UseOnlyAllowedDependenciesAndNamespaces()
    {
        var allowedAssemblyPrefixes = new[] { "System", "TkpSalaryCalculator.Domain" };
        var references = typeof(WorkRecordUseCase).Assembly.GetReferencedAssemblies().Select(x => x.Name!).ToArray();
        Assert.All(references, reference => Assert.Contains(allowedAssemblyPrefixes,
            prefix => reference.StartsWith(prefix, StringComparison.Ordinal)));

        var applicationRoot = Path.Combine(FindRepositoryRoot(), "src", "TkpSalaryCalculator.Application");
        var forbiddenNamespaces = new[]
        {
            "Microsoft.Maui", "Android", "Microsoft.Data.Sqlite", "System.Data.SQLite",
            "TkpSalaryCalculator.Infrastructure", "TkpSalaryCalculator.Presentation"
        };
        var usingPattern = new Regex(@"^\s*(?:global\s+)?using\s+(?<namespace>[A-Za-z0-9_.]+)",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        foreach (var file in Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var namespaces = usingPattern.Matches(source).Select(match => match.Groups["namespace"].Value).ToArray();
            Assert.DoesNotContain(namespaces, value => forbiddenNamespaces.Any(forbidden =>
                value.Equals(forbidden, StringComparison.Ordinal) || value.StartsWith(forbidden + ".", StringComparison.Ordinal)));
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TkpSalaryCalculator.sln"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
