using System.Reflection;
using Microsoft.CodeAnalysis.Testing;

namespace Mediator.Switch.SourceGenerator.Tests;

public static class TestDefinitions
{
    public static readonly ReferenceAssemblies ReferenceAssemblies =
        ReferenceAssemblies.Net.Net90.AddPackages([
            new PackageIdentity("FluentValidation", "11.11.0")
        ]);
    public static readonly Assembly MediatorAssembly = typeof(IRequest<>).Assembly;
}