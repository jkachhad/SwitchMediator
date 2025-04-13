using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class CodeGenerator
{
    public static string Generate(List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        List<((ITypeSymbol Class, ITypeSymbol TResponse) Request, List<(ITypeSymbol Class, ITypeSymbol TRequest,
            ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors)> requestBehaviors,
        List<ITypeSymbol> notifications)
    {
        // Generate fields
        var handlerFields = handlers.Select(h => $"private readonly {h.Class} _{h.Class.GetVariableName()};");

        // Generate behavior fields specific to each request, respecting constraints
        var behaviorFields = requestBehaviors.SelectMany(r =>
        {
            var (request, applicableBehaviors) = r;
            return applicableBehaviors.Select(b =>
                $"private readonly {b.Class.ToString().DropGenerics()}<{request.Class}, {b.TResponse}> _{b.Class.GetVariableName()}__{request.Class.GetVariableName()};");
        });
        
        var notificationHandlerFields = notifications.Select(n =>
            $"private readonly IEnumerable<INotificationHandler<{n}>> _{n.GetVariableName()}__Handlers;");

        // Generate constructor parameters
        var constructorParams = handlers.Select(h => $"{h.Class} {h.Class.GetVariableName()}");
        var behaviorParams = requestBehaviors.SelectMany(r =>
        {
            var (request, applicableBehaviors) = r;
            return applicableBehaviors.Select(b =>
                $"{b.Class.ToString().DropGenerics()}<{request.Class}, {b.TResponse}> {b.Class.GetVariableName()}__{request.Class.GetVariableName()}");
        });
        constructorParams = constructorParams.Concat(behaviorParams);

        // Generate constructor initializers
        var constructorInitializers = handlers.Select(h =>
            $"_{h.Class.GetVariableName()} = {h.Class.GetVariableName()};");
        var behaviorInitializers = requestBehaviors.SelectMany(r =>
        {
            var (request, applicableBehaviors) = r;
            return applicableBehaviors.Select(b =>
                $"_{b.Class.GetVariableName()}__{request.Class.GetVariableName()} = {b.Class.GetVariableName()}__{request.Class.GetVariableName()};");
        });
        constructorInitializers = constructorInitializers.Concat(behaviorInitializers)
            .Concat(notifications.Select(n =>
                $"_{n.GetVariableName()}__Handlers = _serviceProvider.GetServices<INotificationHandler<{n}>>();"));

        // Generate Send method switch cases
        var sendCases = requestBehaviors
            .OrderBy(r => r.Request.Class, Comparers.TypeHierarchyComparer)
            .Select(r =>
            {
                var handler = handlers.FirstOrDefault(h => h.TRequest.Equals(r.Request.Class, SymbolEqualityComparer.Default));
                if (handler == default) return null;
                return $"case {r.Request.Class} {r.Request.Class.GetVariableName()}:\n                return ToResponse<Task<TResponse>>(\n                    Handle{r.Request.Class.Name}WithBehaviors({r.Request.Class.GetVariableName()}, cancellationToken));";
            }).Where(c => c != null);

        // Generate behavior chain methods
        var behaviorMethods = requestBehaviors.Select(r =>
        {
            var (request, applicableBehaviors) = r;
            var handler = handlers.FirstOrDefault(h => h.TRequest.Equals(request.Class, SymbolEqualityComparer.Default));
            if (handler == default) return null;
            var chain = BehaviorChainBuilder.Build(applicableBehaviors, request.Class.GetVariableName(), $"_{handler.Class.GetVariableName()}.Handle");
            return $$"""
                     private Task<{{request.TResponse}}> Handle{{request.Class.Name}}WithBehaviors(
                             {{request.Class}} request,
                             CancellationToken cancellationToken)
                         {
                             return
                                 {{chain}};
                         }
                     """;
        }).Where(m => m != null);

        // Generate Publish method switch cases
        var publishCases = notifications
            .OrderBy(n => n, Comparers.TypeHierarchyComparer)
            .Select(n =>
                $$"""
                  case {{n}} {{n.GetVariableName()}}:
                              {
                                  foreach (var handler in _{{n.GetVariableName()}}__Handlers)
                                  {
                                      await handler.Handle({{n.GetVariableName()}}, cancellationToken);
                                  }
                                  break;
                              }
                  """);

        // Generate the complete SwitchMediator class
        return Normalize(
            $$"""
              //------------------------------------------------------------------------------
              // <auto-generated>
              //     This code was generated by SwitchMediator.
              //
              //     Changes to this file may cause incorrect behavior and will be lost if
              //     the code is regenerated.
              // </auto-generated>
              //------------------------------------------------------------------------------
              
              using System;
              using System.Collections.Generic;
              using System.Diagnostics;
              using Microsoft.Extensions.DependencyInjection;
              using System.Runtime.CompilerServices;
              using System.Threading;
              using System.Threading.Tasks;
              
              namespace Mediator.Switch;
              
              #pragma warning disable CS1998

              public class SwitchMediator : IMediator
              {
                  private readonly IServiceProvider _serviceProvider;
                  {{string.Join("\n    ", handlerFields.Concat(behaviorFields).Concat(notificationHandlerFields))}}
              
                  public SwitchMediator(IServiceProvider serviceProvider,
                      {{string.Join(",\n        ", constructorParams)}})
                  {
                      _serviceProvider = serviceProvider;
                      {{string.Join("\n        ", constructorInitializers)}}
                  }
              
                  public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
                  {
                      switch (request)
                      {
                          {{string.Join("\n            ", sendCases)}}
                          default:
                              throw new ArgumentException($"No handler for {request.GetType().Name}");
                      }
                  }
              
                  public async Task Publish(INotification notification, CancellationToken cancellationToken = default)
                  {
                      switch (notification)
                      {
                          {{string.Join("\n            ", publishCases)}}
                          default:
                              throw new ArgumentException($"No handlers for {notification.GetType().Name}");
                      }
                  }
              
                  {{string.Join("\n\n    ", behaviorMethods)}}
              
                  [MethodImpl(MethodImplOptions.AggressiveInlining)]
                  [DebuggerStepThrough]
                  private T ToResponse<T>(object result)
                  {
                      return (T) result;
                  }
              }
              """);
    }

    private static string Normalize(string code) =>
        string.Join("\n",
            code.Replace("\r\n", "\n").TrimEnd().Split('\n')
                .Select(line => line.TrimEnd()));
}