using System.Diagnostics;
using Xunit;

namespace Mediator.Switch.SourceGenerator.Tests;

public sealed class TheoryRunnableInDebugOnlyAttribute : TheoryAttribute
{
    public TheoryRunnableInDebugOnlyAttribute()
    {
        if (!Debugger.IsAttached)
        {
            Skip = "Only running in interactive mode.";
        }
    }
}