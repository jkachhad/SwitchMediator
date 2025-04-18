using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public class TypeHierarchyComparer : IComparer<ITypeSymbol>
{
    private readonly Dictionary<ITypeSymbol, (int Depth, ITypeSymbol RootType)> _dictionary;

    public TypeHierarchyComparer(ITypeSymbol iRequestType, IEnumerable<ITypeSymbol> types) =>
        _dictionary = types.ToDictionary<ITypeSymbol, ITypeSymbol, (int Depth, ITypeSymbol RootType)>(
            t => t,
            t => GetDepthAndRootType(iRequestType, t),
            SymbolEqualityComparer.Default);

    public int Compare(ITypeSymbol? x, ITypeSymbol? y)
    {
        if (x == null || y == null) throw new InvalidOperationException();
        if (SymbolEqualityComparer.Default.Equals(x, y)) return 0;

        // Get root types for grouping
        var rootX = _dictionary[x].RootType;
        var rootY = _dictionary[y].RootType;

        // Compare by root type name to order groups
        if (!SymbolEqualityComparer.Default.Equals(rootX, rootY))
        {
            return string.Compare(rootX.ToString(), rootY.ToString(), StringComparison.Ordinal);
        }

        // Within the same group, sort by depth
        var depthX = _dictionary[x].Depth;
        var depthY = _dictionary[y].Depth;
        if (depthX > depthY) return -1; // More derived comes first
        if (depthX < depthY) return 1;  // Less derived comes later

        // Same depth, sort by full name
        return string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);
    }

    private static (int Depth, ITypeSymbol RootType) GetDepthAndRootType(ITypeSymbol iRequestType, ITypeSymbol type)
    {
        var depth = 0;
        var current = type;
        while (current.BaseType != null && 
               current.BaseType.AllInterfaces.Any(i =>
                   SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iRequestType)))
        {
            depth++;
            current = current.BaseType;
        }
        return (depth, current);
    }
}
