using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Extensions;

public static class SymbolExtensions
{
    public static string GetVariableName(this ISymbol s, bool lowerCaseFirstChar = true)
    {
        var variableName = s.ToString().DropGenerics().ToVariableName();
        return lowerCaseFirstChar ? variableName.ToLowerFirst() : variableName;
    }
}