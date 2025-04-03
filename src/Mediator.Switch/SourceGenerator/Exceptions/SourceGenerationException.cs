using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Exceptions;

public class SourceGenerationException(string errorMessage, Location location) : Exception(errorMessage)
{
    public Location Location { get; } = location;
}