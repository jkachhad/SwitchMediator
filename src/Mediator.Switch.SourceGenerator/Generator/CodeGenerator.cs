using Mediator.Switch.SourceGenerator.Extensions;
using Microsoft.CodeAnalysis;

namespace Mediator.Switch.SourceGenerator.Generator;

public static class CodeGenerator
{
    public static string Generate(
        ITypeSymbol iRequestType,
        ITypeSymbol iNotificationType,
        List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        List<((ITypeSymbol Class, ITypeSymbol TResponse) Request, List<(ITypeSymbol Class, ITypeSymbol TRequest,
            ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors)> requestBehaviors,
        List<(ITypeSymbol Class, ITypeSymbol TNotification)> notificationHandlers,
        List<ITypeSymbol> notifications,
        List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> behaviors)
    {
        // Generate fields
        var handlerFields = handlers.Select(h => $"private {h.Class}? _{h.Class.GetVariableName()};");

        // Generate behavior fields specific to each request, respecting constraints
        var behaviorFields = requestBehaviors.SelectMany(r =>
        {
            var (request, applicableBehaviors) = r;
            return applicableBehaviors.Select(b =>
                $"private {b.Class.ToString().DropGenerics()}<{request.Class}, {b.TResponse}>? _{b.Class.GetVariableName()}__{request.Class.GetVariableName()};");
        });

        var notificationHandlerFields = notifications.Select(n =>
            $"private IEnumerable<INotificationHandler<{n}>>? _{n.GetVariableName()}__Handlers;");

        // Generate Send method switch cases
        var sendCases = requestBehaviors
            .OrderBy(r => r.Request.Class, new TypeHierarchyComparer(iRequestType, requestBehaviors.Select(r => r.Request.Class)))
            .Select(r => TryGenerateSendCase(iRequestType, handlers, r.Request))
            .Where(c => c != null);

        // Generate behavior chain methods
        var behaviorMethods = requestBehaviors
            .Select(r => TryGenerateBehaviorMethod(handlers, r))
            .Where(m => m != null);

        // Generate Publish method switch cases
        var publishCases = notifications
            .OrderBy(n => n, new TypeHierarchyComparer(iRequestType, notifications.Select(n => n)))
            .Select(n => TryGeneratePublishCase(iNotificationType, notificationHandlers, n))
            .Where(c => c != null);

        // Generate known types
        var requestHandlerTypes = handlers.Select(h => $"typeof({h.Class})");
        var notificationTypes = notifications.Select(n =>
            $"(typeof({n}), new Type[] {{\n                    {string.Join(",\n                    ", notificationHandlers
                .Where(h => h.TNotification.Equals(n, SymbolEqualityComparer.Default))
                .Select(h => $"typeof({h.Class})"))}\n                }})");
        var pipelineBehaviorTypes = requestBehaviors.SelectMany(r =>
        {
            var (request, applicableBehaviors) = r;
            return applicableBehaviors.Select(b =>
                $"typeof({b.Class.ToString().DropGenerics()}<{request.Class}, {b.TResponse}>)");
        });

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
               
               #nullable enable
               
               using System;
               using System.Linq;
               using System.Collections.Generic;
               using System.Diagnostics;
               using System.Runtime.CompilerServices;
               using System.Threading;
               using System.Threading.Tasks;
               
               #if NET8_0_OR_GREATER
               using System.Collections.Frozen;
               #endif
               
               namespace Mediator.Switch;
               
               #pragma warning disable CS1998, CS0169
               
               public class SwitchMediator : IMediator
               {
                   #region Fields
               
                   {{string.Join("\n    ", handlerFields.Concat(behaviorFields).Concat(notificationHandlerFields))}}
               
                   private readonly ISwitchMediatorServiceProvider _svc;
               
                   #endregion
               
                   #region Constructor
               
                   public SwitchMediator(ISwitchMediatorServiceProvider serviceProvider)
                   {
                       _svc = serviceProvider;
                   }
               
                   #endregion
               
                   public static (IReadOnlyList<Type> RequestHandlerTypes, IReadOnlyList<(Type NotificationType, IReadOnlyList<Type> HandlerTypes)> NotificationTypes, IReadOnlyList<Type> PipelineBehaviorTypes) KnownTypes
                   {
                       get { return (SwitchMediatorKnownTypes.RequestHandlerTypes, SwitchMediatorKnownTypes.NotificationTypes, SwitchMediatorKnownTypes.PipelineBehaviorTypes); }
                   }
               
                   public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
                   {
                       if (SendSwitchCase.Cases.TryGetValue(request.GetType(), out var handle))
                       {
                           return (Task<TResponse>)handle(this, request, cancellationToken);
                       }
                       
                       throw new ArgumentException($"No handler for {request.GetType().Name}");
                   }
               
                   private static class SendSwitchCase
                   {
                       public static readonly IDictionary<Type, Func<SwitchMediator, object, CancellationToken, object>> Cases = new (Type, Func<SwitchMediator, object, CancellationToken, object>)[]
                       {
               {{string.Join(",\n", sendCases)}}
                       }
               #if NET8_0_OR_GREATER
                       .ToFrozenDictionary
               #else
                       .ToDictionary
               #endif
                           (t => t.Item1, t => t.Item2);
                   }
               
                   public Task Publish(INotification notification, CancellationToken cancellationToken = default)
                   {
                       if (PublishSwitchCase.Cases.TryGetValue(notification.GetType(), out var handle))
                       {
                           return handle(this, notification, cancellationToken);
                       }
                       
                       return Task.CompletedTask;
                   }
               
                   private static class PublishSwitchCase
                   {
                       public static readonly IDictionary<Type, Func<SwitchMediator, INotification, CancellationToken, Task>> Cases = new (Type, Func<SwitchMediator, INotification, CancellationToken, Task>)[]
                       {
               {{string.Join(",\n", publishCases)}}
                       }
               #if NET8_0_OR_GREATER
                       .ToFrozenDictionary
               #else
                       .ToDictionary
               #endif
                           (t => t.Item1, t => t.Item2);
                   }
               
                   {{string.Join("\n\n    ", behaviorMethods)}}
               
                   [MethodImpl(MethodImplOptions.AggressiveInlining)]
                   [DebuggerStepThrough]
                   private T Get<T>(ref T? field) where T : notnull
                   {
                       return field ?? (field = _svc.Get<T>());
                   }
               
                   /// <summary>
                   /// Provides lists of SwitchMediator component implementation types.
                   /// </summary>
                   public static class SwitchMediatorKnownTypes
                   {
                       public static readonly IReadOnlyList<Type> RequestHandlerTypes =
                           new Type[] {
                               {{string.Join(",\n                ", requestHandlerTypes)}}
                           }.AsReadOnly();
                   
                       public static readonly IReadOnlyList<(Type NotificationType, IReadOnlyList<Type> HandlerTypes)> NotificationTypes = 
                          new (Type NotificationType, IReadOnlyList<Type> HandlerTypes)[] {
                               {{string.Join(",\n                ", notificationTypes)}}
                          }.AsReadOnly();
               
                       public static readonly IReadOnlyList<Type> PipelineBehaviorTypes =
                          new Type[] {
                               {{string.Join(",\n                ", pipelineBehaviorTypes)}}
                          }.AsReadOnly();
                   }    
               }
               """);
    }

    private static string? TryGenerateSendCase(
        ITypeSymbol iRequestType,
        List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        (ITypeSymbol Class, ITypeSymbol TResponse) request)
    {
        var current = request.Class;
        do
        {
            var handler = handlers.FirstOrDefault(h =>
                h.TRequest.Equals(current, SymbolEqualityComparer.Default));
            if (handler != default)
            {
                return $$"""
                                     ( // case {{request.Class}}:
                                         typeof({{request.Class}}), (instance, request, cancellationToken) =>
                                             instance.Handle_{{current.GetVariableName(false)}}(
                                                 ({{request.Class}}) request, cancellationToken)
                                     )
                         """;
            }
            current = current.BaseType;
        } while (current != null &&
                 current.AllInterfaces.Any(i =>
                     SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iRequestType)));

        return null;
    }

    private static string? TryGeneratePublishCase(
        ITypeSymbol iNotificationType,
        List<(ITypeSymbol Class, ITypeSymbol TNotification)> notificationHandlers,
        ITypeSymbol notification)
    {
        var current = notification;
        do
        {
            var handler = notificationHandlers.FirstOrDefault(h =>
                h.TNotification.Equals(current, SymbolEqualityComparer.Default));
            if (handler != default)
            {
                return $$"""
                                     ( // case {{notification}}:
                                         typeof({{notification}}), async (instance, notification, cancellationToken) =>
                                         {
                                             foreach (var handler in instance.Get(ref instance._{{current.GetVariableName()}}__Handlers))
                                             {
                                                 await handler.Handle(({{notification}})notification, cancellationToken);
                                             }
                                         }
                                     )
                         """;
            }
            current = current.BaseType;
        } while (current != null &&
                 current.AllInterfaces.Any(i =>
                     SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iNotificationType)));

        return null;
    }

    private static string? TryGenerateBehaviorMethod(List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse)> handlers,
        ((ITypeSymbol Class, ITypeSymbol TResponse) Request, List<(ITypeSymbol Class, ITypeSymbol TRequest, ITypeSymbol TResponse, IReadOnlyList<ITypeParameterSymbol> TypeParameters)> Behaviors) r)
    {
        var (request, applicableBehaviors) = r;
        var handler = handlers.FirstOrDefault(h => h.TRequest.Equals(request.Class, SymbolEqualityComparer.Default));
        if (handler == default) return null;
        var chain = BehaviorChainBuilder.Build(applicableBehaviors, request.Class.GetVariableName(), $"Get(ref _{handler.Class.GetVariableName()}).Handle");
        return $$"""
                 private Task<{{request.TResponse}}> Handle_{{request.Class.GetVariableName(false)}}(
                         {{request.Class}} request,
                         CancellationToken cancellationToken)
                     {
                         return
                             {{chain}};
                     }
                 """;
    }

    private static string Normalize(string code) =>
        string.Join(Environment.NewLine,
            code.Replace("\r\n", "\n") // Normalize Windows endings
                .Split('\n')
                .Select(line => line.TrimEnd()));
}