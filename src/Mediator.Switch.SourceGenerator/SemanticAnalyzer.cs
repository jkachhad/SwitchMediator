using Mediator.Switch.SourceGenerator.Exceptions;
using Mediator.Switch.SourceGenerator.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mediator.Switch.SourceGenerator;

public class SemanticAnalyzer
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _iMediatorSymbol;
    private readonly INamedTypeSymbol _iSenderSymbol;
    private readonly INamedTypeSymbol _iPublisherSymbol;
    private readonly INamedTypeSymbol _iRequestSymbol;
    private readonly INamedTypeSymbol _iRequestHandlerSymbol;
    private readonly INamedTypeSymbol _iPipelineBehaviorSymbol;
    private readonly INamedTypeSymbol _iNotificationSymbol;
    private readonly INamedTypeSymbol _iNotificationHandlerSymbol;
    private readonly INamedTypeSymbol _responseAdaptorAttributeSymbol;
    private readonly INamedTypeSymbol _orderAttributeSymbol;
    private readonly INamedTypeSymbol _requestHandlerAttributeSymbol;

    public SemanticAnalyzer(Compilation compilation)
    {
        _compilation = compilation;

        _iMediatorSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IMediator") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IMediator");
        _iSenderSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.ISender") ?? throw new InvalidOperationException("Could not find Mediator.Switch.ISender");
        _iPublisherSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IPublisher") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IPublisher");
        _iRequestSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequest`1") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IRequest`1");
        _iRequestHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IRequestHandler`2") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IRequestHandler`2");
        _iPipelineBehaviorSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.IPipelineBehavior`2") ?? throw new InvalidOperationException("Could not find Mediator.Switch.IPipelineBehavior`2");
        _iNotificationSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotification") ?? throw new InvalidOperationException("Could not find Mediator.Switch.INotification");
        _iNotificationHandlerSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.INotificationHandler`1") ?? throw new InvalidOperationException("Could not find Mediator.Switch.INotificationHandler`1");
        _orderAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.PipelineBehaviorOrderAttribute") ?? throw new InvalidOperationException("Could not find Mediator.Switch.PipelineBehaviorOrderAttribute");
        _responseAdaptorAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.PipelineBehaviorResponseAdaptorAttribute") ?? throw new InvalidOperationException("Could not find Mediator.Switch.PipelineBehaviorResponseAdaptorAttribute");
        _requestHandlerAttributeSymbol = compilation.GetTypeByMetadataName("Mediator.Switch.RequestHandlerAttribute") ?? throw new InvalidOperationException("Could not find Mediator.Switch.RequestHandlerAttribute");
    }

    public (
        List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, bool hasMediatorRefInCtor)> Handlers,
        List<((ITypeSymbol Class, ITypeSymbol TResponse) Request, List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors)> RequestBehaviors,
        List<(ITypeSymbol NotificationClass, bool HasHandlerWithMediatorDependency)> Notifications
    ) Analyze(List<TypeDeclarationSyntax> types, CancellationToken cancellationToken)
    {
        var requests = new List<(ITypeSymbol Class, ITypeSymbol TResponse)>();
        var handlers = new List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, bool hasMediatorRefInCtor)>();
        var behaviors = new List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)>();
        var foundNotificationTypes = new List<ITypeSymbol>();
        var notificationHandlerInfos = new List<(ITypeSymbol HandlerClass, ITypeSymbol NotificationHandledType)>();

        foreach (var typeSyntax in types)
        {
            if (cancellationToken.IsCancellationRequested) break;
            AnalyzeTypeSyntax(typeSyntax, cancellationToken, requests, handlers, behaviors, foundNotificationTypes, notificationHandlerInfos);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var requestBehaviors = ProcessRequestBehaviors(requests, behaviors);
        var finalNotifications = DetermineNotificationHandlerDependencies(foundNotificationTypes, notificationHandlerInfos, cancellationToken);

        return (handlers, requestBehaviors, finalNotifications);
    }

    private void AnalyzeTypeSyntax(
        TypeDeclarationSyntax typeSyntax,
        CancellationToken cancellationToken,
        List<(ITypeSymbol Class, ITypeSymbol TResponse)> requests,
        List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, bool hasMediatorRefInCtor)> handlers,
        List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors,
        List<ITypeSymbol> foundNotificationTypes,
        List<(ITypeSymbol HandlerClass, ITypeSymbol NotificationHandledType)> notificationHandlerInfos)
    {
        var model = _compilation.GetSemanticModel(typeSyntax.SyntaxTree);
        if (model.GetDeclaredSymbol(typeSyntax, cancellationToken) is not { } typeSymbol || typeSymbol.IsAbstract)
        {
            // Skip non-named types or abstract classes early
            return;
        }

        // Check for different Mediator-related interface implementations
        TryAddRequest(typeSymbol, requests);
        TryAddRequestHandler(typeSymbol, handlers);
        TryAddPipelineBehavior(typeSymbol, behaviors);
        TryAddNotification(typeSymbol, foundNotificationTypes);
        TryAddNotificationHandlerInfo(typeSymbol, notificationHandlerInfos);
    }

    private void TryAddRequest(INamedTypeSymbol typeSymbol, List<(ITypeSymbol Class, ITypeSymbol TResponse)> requests)
    {
        var requestInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, _iRequestSymbol));

        // Ensure it's a concrete implementation of IRequest<T>
        if (requestInterface != null && typeSymbol.TypeArguments.Length == 0)
        {
            var tResponse = requestInterface.TypeArguments[0];
            requests.Add((typeSymbol, tResponse));
        }
    }

    private void TryAddRequestHandler(INamedTypeSymbol typeSymbol, List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, bool hasMediatorRefInCtor)> handlers)
    {
        var handlerInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, _iRequestHandlerSymbol));

        // Ensure it's a concrete implementation of IRequestHandler<TRequest, TResponse>
        if (handlerInterface != null && typeSymbol.TypeArguments.Length == 0)
        {
            var tRequest = handlerInterface.TypeArguments[0];
            var tResponse = handlerInterface.TypeArguments[1];
            VerifyRequestMatchesHandler(typeSymbol, tRequest); // Verify attribute if present
            var hasDependency = ConstructorHasMediatorDependencies(typeSymbol, out _);
            handlers.Add((typeSymbol, tRequest, tResponse, hasDependency));
        }
    }

    private void TryAddPipelineBehavior(INamedTypeSymbol typeSymbol, List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors)
    {
        var behaviorInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, _iPipelineBehaviorSymbol));

        // Ensure it's a concrete implementation of IPipelineBehavior<TRequest, TResponse>
        if (behaviorInterface != null) // Abstract check is done in AnalyzeTypeSyntax
        {
            var tRequest = behaviorInterface.TypeArguments[0];
            var tResponse = behaviorInterface.TypeArguments[1];
            var typeParameters = typeSymbol.TypeParameters;
            VerifyAdaptorMatchesTResponse(typeSymbol, tResponse); // Verify attribute if present
            VerifyConstructorDoesNotHaveMediatorDependencies(typeSymbol); // Verify no forbidden dependencies
            behaviors.Add((typeSymbol, tRequest, tResponse, typeParameters));
        }
    }

    private void TryAddNotification(INamedTypeSymbol typeSymbol, List<ITypeSymbol> foundNotificationTypes)
    {
        var notificationInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, _iNotificationSymbol));

        // Ensure it's a concrete implementation of INotification
        if (notificationInterface != null && typeSymbol.TypeArguments.Length == 0)
        {
            // Avoid adding duplicates if a type appears multiple times (e.g., partial classes)
            if (!foundNotificationTypes.Contains(typeSymbol, SymbolEqualityComparer.Default))
            {
                foundNotificationTypes.Add(typeSymbol);
            }
        }
    }

    private void TryAddNotificationHandlerInfo(INamedTypeSymbol typeSymbol, List<(ITypeSymbol HandlerClass, ITypeSymbol NotificationHandledType)> notificationHandlerInfos)
    {
        var notificationHandlerInterface = typeSymbol.AllInterfaces.FirstOrDefault(i =>
            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, _iNotificationHandlerSymbol));

        // Ensure it's a concrete implementation of INotificationHandler<TNotification>
        if (notificationHandlerInterface != null && typeSymbol.TypeArguments.Length == 0)
        {
            var notificationHandledType = notificationHandlerInterface.TypeArguments.FirstOrDefault();
            if (notificationHandledType != null)
            {
                notificationHandlerInfos.Add((HandlerClass: typeSymbol, NotificationHandledType: notificationHandledType));
            }
        }
    }

    private List<((ITypeSymbol Class, ITypeSymbol TResponse) Request, List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors)>
        ProcessRequestBehaviors(
            List<(ITypeSymbol Class, ITypeSymbol TResponse)> requests,
            List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors)
    {
        return requests.Select(request =>
            (Request: request, Behaviors: behaviors
                .Select(b =>
                {
                    var actualTResponse = TryUnwrapRequestTResponse(b, request);
                    // Use 'default' for value tuple if unwrapping fails
                    return actualTResponse != null ? b with { TResponse = actualTResponse } : default;
                })
                // Filter out non-applicable behaviors (where unwrapping failed or constraints don't match)
                .Where(b => b != default && BehaviorApplicabilityChecker.IsApplicable(_compilation, b.TypeParameters, request.Class, b.TResponse))
                .OrderByDescending(b => GetOrder(b.Class)) // Order by attribute
                .ToList()))
            .ToList();
    }

    private List<(ITypeSymbol NotificationClass, bool HasHandlerWithMediatorDependency)>
        DetermineNotificationHandlerDependencies(
            List<ITypeSymbol> foundNotificationTypes,
            List<(ITypeSymbol HandlerClass, ITypeSymbol NotificationHandledType)> notificationHandlerInfos,
            CancellationToken cancellationToken)
    {
        var finalNotifications = new List<(ITypeSymbol NotificationClass, bool HasHandlerWithMediatorDependency)>();

        foreach (var notificationType in foundNotificationTypes)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Find handlers specifically for this notification type
            var specificHandlers = notificationHandlerInfos
                .Where(info => SymbolEqualityComparer.Default.Equals(info.NotificationHandledType, notificationType))
                .Select(info => info.HandlerClass)
                .OfType<INamedTypeSymbol>();

            // Check if any of these handlers have the dependency
            var anyHandlerHasDependency = false;
            foreach (var handlerClass in specificHandlers)
            {
                if (ConstructorHasMediatorDependencies(handlerClass, out _))
                {
                    anyHandlerHasDependency = true;
                    break; // Optimization: stop checking once one is found
                }
            }
            finalNotifications.Add((NotificationClass: notificationType, HasHandlerWithMediatorDependency: anyHandlerHasDependency));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return finalNotifications;
    }
    
    private void VerifyRequestMatchesHandler(INamedTypeSymbol classSymbol, ITypeSymbol handledType)
    {
        var requestHandlerAttribute = handledType.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _requestHandlerAttributeSymbol));

        if (requestHandlerAttribute == null || requestHandlerAttribute.ConstructorArguments.Length == 0) return;

        if (requestHandlerAttribute.ConstructorArguments.Single().Value is INamedTypeSymbol attributeSymbol)
        {
            if (!SymbolEqualityComparer.Default.Equals(attributeSymbol, classSymbol))
            {
                var syntaxReference = requestHandlerAttribute.ApplicationSyntaxReference;
                throw new SourceGenerationException(
                    $"RequestHandlerAttribute: Handler type mismatch on '{handledType.ToDisplayString()}' - expecting handler '{classSymbol.ToDisplayString()}' but attribute points to '{attributeSymbol.ToDisplayString()}'.",
                    syntaxReference != null ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span) : Location.None);
            }
        }
    }

    private void VerifyAdaptorMatchesTResponse(INamedTypeSymbol classSymbol, ITypeSymbol tResponse)
    {
        var responseTypeAdaptorAttribute = classSymbol.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _responseAdaptorAttributeSymbol));

        if (responseTypeAdaptorAttribute == null) return;

        var responseWrapperType = GetAdaptorWrapperType(responseTypeAdaptorAttribute);
        if (tResponse is not INamedTypeSymbol unwrappedResponseType ||
            !SymbolEqualityComparer.Default.Equals(unwrappedResponseType.OriginalDefinition, responseWrapperType.OriginalDefinition))
        {
            var syntaxReference = responseTypeAdaptorAttribute.ApplicationSyntaxReference;
            throw new SourceGenerationException($"{nameof(PipelineBehaviorResponseAdaptorAttribute)}.{nameof(PipelineBehaviorResponseAdaptorAttribute.GenericsType)} does not match IPipelineBehavior's TResponse argument.",
                syntaxReference != null ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span) : Location.None);
        }
    }

    private void VerifyConstructorDoesNotHaveMediatorDependencies(INamedTypeSymbol classSymbol)
    {
        if (!ConstructorHasMediatorDependencies(classSymbol, out var constructor)) return;

        var syntaxReference = constructor!.DeclaringSyntaxReferences.FirstOrDefault();
        var location = Location.None;
        if (syntaxReference != null) location = Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span);

        throw new SourceGenerationException($"PipelineBehavior '{classSymbol.ToDisplayString()}' constructors are not allowed to take dependencies on IMediator, ISender, or IPublisher.", location);
    }

    private ITypeSymbol? TryUnwrapRequestTResponse(
        (ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters) b,
        (ITypeSymbol Class, ITypeSymbol TResponse) request)
    {
        var responseTypeAdaptorAttribute = b.Class.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _responseAdaptorAttributeSymbol));

        if (responseTypeAdaptorAttribute == null) return request.TResponse;

        var responseWrapperType = GetAdaptorWrapperType(responseTypeAdaptorAttribute);
        if (request.TResponse is INamedTypeSymbol unwrappedResponseType &&
            SymbolEqualityComparer.Default.Equals(unwrappedResponseType.OriginalDefinition, responseWrapperType.OriginalDefinition))
        {
            return unwrappedResponseType.TypeArguments.Length == 1 ? unwrappedResponseType.TypeArguments[0] : null;
        }

        return null;
    }

    private static INamedTypeSymbol GetAdaptorWrapperType(AttributeData responseTypeAdaptorAttribute)
    {
        if (responseTypeAdaptorAttribute.ConstructorArguments.Length != 1 ||
            responseTypeAdaptorAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol typeArgSymbol)
        {
             var syntaxReference = responseTypeAdaptorAttribute.ApplicationSyntaxReference;
             throw new SourceGenerationException($"{nameof(PipelineBehaviorResponseAdaptorAttribute)} requires a single 'typeof(UnboundGeneric<>)' argument.",
                     syntaxReference != null ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span) : Location.None);
        }

        if (!typeArgSymbol.IsUnboundGenericType || typeArgSymbol.TypeParameters.Length != 1)
        {
            var syntaxReference = responseTypeAdaptorAttribute.ApplicationSyntaxReference;
            throw new SourceGenerationException($"{nameof(PipelineBehaviorResponseAdaptorAttribute)}.{nameof(PipelineBehaviorResponseAdaptorAttribute.GenericsType)} must be an unbound generic type with 1 argument (e.g., typeof(Wrapper<>)).",
                    syntaxReference != null ? Location.Create(syntaxReference.SyntaxTree, syntaxReference.Span) : Location.None);
        }

        return typeArgSymbol;
    }

    private bool ConstructorHasMediatorDependencies(INamedTypeSymbol typeSymbol, out IMethodSymbol? ctor)
    {
        ctor = null;
        foreach (var constructor in typeSymbol.Constructors.Where(c => !c.IsStatic))
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.Type.OriginalDefinition is not {} originalParamTypeDef) continue; // Skip if type or definition is null

                if (SymbolEqualityComparer.Default.Equals(originalParamTypeDef, _iMediatorSymbol) ||
                    SymbolEqualityComparer.Default.Equals(originalParamTypeDef, _iSenderSymbol) ||
                    SymbolEqualityComparer.Default.Equals(originalParamTypeDef, _iPublisherSymbol))
                {
                    ctor = constructor;
                    return true;
                }
            }
        }
        return false;
    }

    private int GetOrder(ITypeSymbol typeSymbol)
    {
        var orderAttribute = typeSymbol.GetAttributes()
            .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _orderAttributeSymbol));

        return orderAttribute?.ConstructorArguments.Length > 0 && orderAttribute.ConstructorArguments[0].Value is int order
               ? order
               : int.MaxValue; // Default order if attribute is missing or invalid
    }
}
