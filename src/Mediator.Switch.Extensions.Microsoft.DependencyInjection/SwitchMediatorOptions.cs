﻿using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Extensions.Microsoft.DependencyInjection;

public class SwitchMediatorOptions
{
    /// <summary>
    /// The assemblies scanned for handlers and behaviors.
    /// </summary>
    public Assembly[] TargetAssemblies { get; set; } = [];
    
    /// <summary>
    /// The default lifetime for the services registered by the mediator.
    /// </summary>
    public ServiceLifetime ServiceLifetime { get; set; } = ServiceLifetime.Scoped;
    
    /// <summary>
    /// Specify explicit notification handler ordering. Any other handlers not specified will assume lower priority.
    /// </summary>
    /// <param name="handlerTypes">Handler types in the preferred order.</param>
    /// <typeparam name="TNotification"></typeparam>
    /// <returns></returns>
    public SwitchMediatorOptions OrderNotificationHandlers<TNotification>(params Type[] handlerTypes)
        where TNotification : INotification
    {
        if (handlerTypes.Length == 0)
        {
            return this;
        }

        if (handlerTypes.Distinct().Count() != handlerTypes.Length)
        {
            throw new ArgumentException("Duplicate notification handler types are not allowed.", nameof(handlerTypes));
        }
        
        foreach (var handlerType in handlerTypes)
        {
            if (!typeof(INotificationHandler<TNotification>).IsAssignableFrom(handlerType))
            {
                throw new ArgumentException($"Type {handlerType.Name} does not implement INotificationHandler<{typeof(TNotification).Name}>");
            }
        }

        OrderedNotificationHandlers.Add(typeof(TNotification), handlerTypes);
        return this;
    }

    internal Dictionary<Type, Type[]> OrderedNotificationHandlers { get; } = new();
}