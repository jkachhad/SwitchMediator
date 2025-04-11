using Mediator.Switch.SourceGenerator.Exceptions;
using Mediator.Switch.SourceGenerator.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mediator.Switch.SourceGenerator;

public class SemanticAnalyzer
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _iRequestSymbol;
    private readonly INamedTypeSymbol _iRequestHandlerSymbol;
    private readonly INamedTypeSymbol _iPipelineBehaviorSymbol;
    private readonly INamedTypeSymbol _iNotificationSymbol;
    private readonly INamedTypeSymbol _responseAdaptorAttributeSymbol;
    private readonly INamedTypeSymbol _orderAttributeSymbol;
    private readonly INamedTypeSymbol _requestHandlerAttributeSymbol;

    public SemanticAnalyzer(Compilation compilation)
    {
        _compilation = compilation;
            
        var iRequestSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequest`1"); 
        var iRequestHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequestHandler`2");
        var iPipelineBehaviorSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IPipelineBehavior`2");
        var iNotificationSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotification");
        var iNotificationHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotificationHandler`1"); 
        
        var orderAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.PipelineBehaviorOrderAttribute");
        var responseAdaptorAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.PipelineBehaviorResponseAdaptorAttribute");
        var requestHandlerAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.RequestHandlerAttribute");

        if (iRequestSymbol == null || iRequestHandlerSymbol == null || iPipelineBehaviorSymbol == null ||
            iNotificationSymbol == null || iNotificationHandlerSymbol == null || orderAttributeSymbol == null ||
            responseAdaptorAttributeSymbol == null || requestHandlerAttributeSymbol == null)
        {
            throw new InvalidOperationException("Could not find required Mediator.Switch types.");
        }

        _iRequestSymbol = iRequestSymbol;
        _iRequestHandlerSymbol = iRequestHandlerSymbol;
        _iPipelineBehaviorSymbol = iPipelineBehaviorSymbol;
        _iNotificationSymbol = iNotificationSymbol;
        _orderAttributeSymbol = orderAttributeSymbol;
        _responseAdaptorAttributeSymbol = responseAdaptorAttributeSymbol;
        _requestHandlerAttributeSymbol = requestHandlerAttributeSymbol;
    }

    public (List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> Handlers, List<((ITypeSymbol Class,
            ITypeSymbol TResponse) Request,
            List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol>
                TypeParameters)> Behaviors)> RequestBehaviors, List<ITypeSymbol> Notifications)
        Analyze(List<TypeDeclarationSyntax> types, CancellationToken cancellationToken)
    {
        var requests = new List<(ITypeSymbol Class, ITypeSymbol TResponse)>();
        var handlers = new List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)>();
        var behaviors = new List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)>();
        var notifications = new List<ITypeSymbol>();

        foreach (var typeSyntax in types)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            var model = _compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var typeSymbol = model.GetDeclaredSymbol(typeSyntax, cancellationToken);
            if (typeSymbol is not {Kind: SymbolKind.NamedType})
                continue;

            var requestInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.Equals(_iRequestSymbol, SymbolEqualityComparer.Default));
            if (requestInterface != null && typeSymbol.TypeArguments.Length == 0)
            {
                var tResponse = requestInterface.TypeArguments[0];
                requests.Add((typeSymbol, tResponse));
            }

            var handlerInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.Equals(_iRequestHandlerSymbol, SymbolEqualityComparer.Default));
            if (handlerInterface != null && typeSymbol.TypeArguments.Length == 0 && !typeSymbol.IsAbstract)
            {
                var tRequest = handlerInterface.TypeArguments[0];
                var tResponse = handlerInterface.TypeArguments[1];
                VerifyRequestMatchesHandler(typeSymbol, tRequest);
                handlers.Add((typeSymbol, tRequest, tResponse));
            }

            var behaviorInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.Equals(_iPipelineBehaviorSymbol, SymbolEqualityComparer.Default));
            if (behaviorInterface != null && !typeSymbol.IsAbstract)
            {
                var tRequest = behaviorInterface.TypeArguments[0];
                var tResponse = behaviorInterface.TypeArguments[1];
                var typeParameters = typeSymbol.TypeParameters; // Capture constraints
                VerifyAdaptorMatchesTResponse(typeSymbol, tResponse);
                behaviors.Add((typeSymbol, tRequest, tResponse, typeParameters));
            }

            var notificationInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.Equals(_iNotificationSymbol, SymbolEqualityComparer.Default));
            if (notificationInterface != null && typeSymbol.TypeArguments.Length == 0)
            {
                notifications.Add(typeSymbol);
            }
        }

        var requestBehaviors = requests.Select(request =>
                (Request: request, Behaviors: behaviors
                    .Select(b =>
                    {
                        var actualTResponse = TryUnwrapRequestTResponse(b, request);
                        return actualTResponse != null
                            ? b with {TResponse = actualTResponse}
                            : default;
                    })
                    .Where(b => b != default && 
                                BehaviorApplicabilityChecker.IsApplicable(_compilation, b.TypeParameters, request.Class, b.TResponse))
                    .OrderBy(_orderAttributeSymbol)
                    .ToList()))
            .ToList();

        return (handlers, requestBehaviors, notifications);
    }

    private void VerifyRequestMatchesHandler(INamedTypeSymbol classSymbol, ITypeSymbol handledType)
    {
        var requestHandlerAttribute = handledType.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Equals(_requestHandlerAttributeSymbol, SymbolEqualityComparer.Default) ?? false);

        if (requestHandlerAttribute == null)
            return;

        var attributeSymbol = (INamedTypeSymbol) requestHandlerAttribute.ConstructorArguments.Single().Value!;
        if (!attributeSymbol.Equals(classSymbol, SymbolEqualityComparer.Default))
        {
            var syntaxReference = requestHandlerAttribute.ApplicationSyntaxReference;
            throw new SourceGenerationException(
                $"RequestHandlerAttribute: Handler type mismatch - expecting {classSymbol.ToDisplayString()}",
                syntaxReference != null
                    ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span)
                    : Location.None);
        }
    }

    private void VerifyAdaptorMatchesTResponse(INamedTypeSymbol classSymbol, ITypeSymbol tResponse)
    {
        var responseTypeAdaptorAttribute = classSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Equals(_responseAdaptorAttributeSymbol, SymbolEqualityComparer.Default) ?? false);
        
        if (responseTypeAdaptorAttribute == null)
            return;
        
        var responseWrapperType = GetAdaptorWrapperType(responseTypeAdaptorAttribute);
        if (tResponse is not INamedTypeSymbol unwrappedResponseType ||
            !SymbolEqualityComparer.Default.Equals(unwrappedResponseType.OriginalDefinition, responseWrapperType.OriginalDefinition))
        {
            var syntaxReference = responseTypeAdaptorAttribute.ApplicationSyntaxReference;
            throw new SourceGenerationException($"{nameof(PipelineBehaviorResponseAdaptorAttribute)}.{nameof(PipelineBehaviorResponseAdaptorAttribute.GenericsType)} does not match IPipelineBehavior's TResponse argument.",
                syntaxReference != null
                    ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span)
                    : Location.None);
        }
    }

    private ITypeSymbol? TryUnwrapRequestTResponse(
        (ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters) b,
        (ITypeSymbol Class, ITypeSymbol TResponse) request)
    {
        var responseTypeAdaptorAttribute = b.Class.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Equals(_responseAdaptorAttributeSymbol, SymbolEqualityComparer.Default) ?? false);
        
        if (responseTypeAdaptorAttribute == null)
            return request.TResponse;
        
        var responseWrapperType = GetAdaptorWrapperType(responseTypeAdaptorAttribute);
        if (request.TResponse is INamedTypeSymbol unwrappedResponseType && 
            SymbolEqualityComparer.Default.Equals(unwrappedResponseType.OriginalDefinition, responseWrapperType.OriginalDefinition))
            return unwrappedResponseType.TypeArguments[0];

        return null;
    }

    private static INamedTypeSymbol GetAdaptorWrapperType(AttributeData responseTypeAdaptorAttribute)
    {
        var typeArgSymbol = (INamedTypeSymbol) responseTypeAdaptorAttribute.ConstructorArguments.Single().Value!;

        if (!typeArgSymbol.IsUnboundGenericType || typeArgSymbol.TypeArguments.Length != 1)
        {
            var syntaxReference = responseTypeAdaptorAttribute.ApplicationSyntaxReference;
            throw new SourceGenerationException($"{nameof(PipelineBehaviorResponseAdaptorAttribute)}.{nameof(PipelineBehaviorResponseAdaptorAttribute.GenericsType)}  must be an unbound generic type with 1 argument.",
                    syntaxReference != null
                        ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span)
                        : Location.None);
        }

        return typeArgSymbol;
    }
}