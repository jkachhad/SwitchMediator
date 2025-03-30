using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Exceptions;

public class SourceGenerationException : Exception
{
    public Location Location { get; }

    public SourceGenerationException(string errorMessage, Location location) : base(errorMessage)
    {
        Location = location;
    }
}