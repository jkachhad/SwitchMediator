using System.Diagnostics;
using Xunit;

namespace Mediator.Switch.SourceGenerator.Tests;

public class TheoryRunnableInDebugOnlyAttribute : TheoryAttribute
{
    public TheoryRunnableInDebugOnlyAttribute()
    {
        if (!Debugger.IsAttached)
        {
            Skip = "Only running in interactive mode.";
        }
    }
}