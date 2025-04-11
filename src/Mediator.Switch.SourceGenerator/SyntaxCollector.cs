using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mediator.Switch.SourceGenerator;

public class SyntaxCollector : ISyntaxReceiver
{
    public List<TypeDeclarationSyntax> Types { get; } = [];

    public void OnVisitSyntaxNode(SyntaxNode node)
    {
        // Include only type declarations that are classes or records
        if (node is TypeDeclarationSyntax typeDeclaration and (ClassDeclarationSyntax or RecordDeclarationSyntax))
        {
            Types.Add(typeDeclaration);
        }
    }
}