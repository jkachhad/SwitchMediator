using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mediator.Switch.Extensions.Microsoft.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers SwitchMediator services using pre-discovered types, avoiding runtime assembly scanning.
    /// </summary>
    /// <typeparam name="TSwitchMediator">The concrete implementation type of IMediator to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceLifetime">The service lifetime for the mediator and its handlers/behaviors.</param>
    /// <param name="knownTypes">A tuple containing pre-discovered lists of request handler, notification handler, and pipeline behavior types. Typically provided by a source generator.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMediator<TSwitchMediator>(this IServiceCollection services, ServiceLifetime serviceLifetime, (IReadOnlyList<Type> RequestHandlerTypes, IReadOnlyList<Type> NotificationHandlerTypes, IReadOnlyList<Type> PipelineBehaviorTypes) knownTypes)
        where TSwitchMediator : class, IMediator =>
        AddMediator<TSwitchMediator>(services, op =>
        {
            op.KnownTypes = knownTypes;
            op.ServiceLifetime = serviceLifetime;
        });

    /// <summary>
    /// Registers SwitchMediator services by scanning the specified assemblies for handlers and behaviors.
    /// </summary>
    /// <typeparam name="TSwitchMediator">The concrete implementation type of IMediator to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceLifetime">The service lifetime for the mediator and its handlers/behaviors.</param>
    /// <param name="assembliesToScan">The assemblies to scan for handlers and behaviors.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMediator<TSwitchMediator>(this IServiceCollection services, ServiceLifetime serviceLifetime, params Assembly[] assembliesToScan)
        where TSwitchMediator : class, IMediator =>
        AddMediator<TSwitchMediator>(services, op =>
        {
            op.TargetAssemblies = assembliesToScan;
            op.ServiceLifetime = serviceLifetime;
        });

    /// <summary>
    /// Registers SwitchMediator services, allowing detailed configuration via the <paramref name="configure"/> action.
    /// This is the primary configuration method, enabling specification of service lifetime, assemblies to scan,
    /// or pre-discovered known types, and ordered notification handlers.
    /// </summary>
    /// <typeparam name="TSwitchMediator">The concrete implementation type of IMediator to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure SwitchMediator options, such as service lifetime, assemblies to scan, pre-discovered known types, or ordered notification handlers.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMediator<TSwitchMediator>(this IServiceCollection services, Action<SwitchMediatorOptions>? configure)
        where TSwitchMediator : class, IMediator
    {
        var options = new SwitchMediatorOptions();

        configure?.Invoke(options);

        services.Add(new ServiceDescriptor(typeof(IMediator), typeof(TSwitchMediator), options.ServiceLifetime));
        services.Add(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<IMediator>(), options.ServiceLifetime));
        services.Add(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<IMediator>(), options.ServiceLifetime));

        if (options.KnownTypes != default)
        {
            RegisterRequestHandlers(services, options.KnownTypes.RequestHandlerTypes, options);
            RegisterNotificationHandlers(services, options.KnownTypes.NotificationHandlerTypes, options);
            RegisterPipelineBehaviors(services, options.KnownTypes.PipelineBehaviorTypes, options);
        }

        if (options.TargetAssemblies.Length > 0)
        {
            var allTypes = GetAllTypesFromAssemblies(options.TargetAssemblies);

            RegisterRequestHandlers(services, allTypes.Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))), options);
            
            RegisterNotificationHandlers(services, allTypes
                .Where(t => t.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
                .ToList(), options);
            
            RegisterPipelineBehaviors(services, allTypes.Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))), options);
        }

        return services;
    }

    private static List<Type> GetAllTypesFromAssemblies(Assembly[] assemblies)
    {
        var allTypes = assemblies
            .Where(assembly => assembly != null)
            .Distinct()
            .SelectMany(assembly =>
            {
                try
                {
                    return assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException)
                {
                    return [];
                }
            })
            .Where(t => t is {IsClass: true, IsAbstract: false} &&
                        t.GetInterfaces()
                            .Any(i => i.IsGenericType &&
                                      (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                                       i.GetGenericTypeDefinition() == typeof(INotificationHandler<>) ||
                                       i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))))
            .ToList();
        return allTypes;
    }

    private static void RegisterRequestHandlers(IServiceCollection services, IEnumerable<Type> requestHandlerTypes, SwitchMediatorOptions options)
    {
        foreach (var handlerType in requestHandlerTypes)
        {
            services.TryAdd(new ServiceDescriptor(handlerType, handlerType, options.ServiceLifetime));
            services.TryAdd(new ServiceDescriptor(typeof(Lazy<>).MakeGenericType(handlerType),
                BuildLazyFactoryDelegate(handlerType), options.ServiceLifetime));
        }
    }

    private static void RegisterNotificationHandlers(
        IServiceCollection services,
        IReadOnlyList<Type> notificationHandlerTypes,
        SwitchMediatorOptions options)
    {
        // 1. Register individual concrete handlers
        foreach (var handlerType in notificationHandlerTypes)
        {
            services.TryAdd(new ServiceDescriptor(handlerType, handlerType, options.ServiceLifetime));
        }

        // 2. Group handlers by the specific notification type they implement
        var handlersByNotificationType = notificationHandlerTypes
            .SelectMany(handlerType => handlerType.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
                .Select(i => (NotificationType: i.GetGenericArguments()[0], HandlerType: handlerType)))
            .GroupBy(x => x.NotificationType)
            .ToDictionary(g => g.Key, g => g.Select(x => x.HandlerType).Distinct().ToArray());

        // 3. Get MethodInfo for the internal static OrderNotificationHandlers<TNotification> method
        var orderMethodInfo = typeof(ServiceCollectionExtensions).GetMethod(nameof(OrderNotificationHandlers), BindingFlags.Static | BindingFlags.NonPublic);
        if (orderMethodInfo == null)
        {
            throw new InvalidOperationException("Could not find the internal static 'OrderNotificationHandlers' method via reflection.");
        }

        // 4. Dynamically call the internal static method for each notification type
        foreach (var kvp in handlersByNotificationType)
        {
            var notificationType = kvp.Key; // TNotification
            var specificHandlerTypes = kvp.Value; // Type[] implementing INotificationHandler<TNotification>
            if (options.OrderedNotificationHandlers.TryGetValue(notificationType, out var orderedHandlerTypes))
            {
                Sort(specificHandlerTypes, orderedHandlerTypes);
            }
            
            try
            {
                // Create a closed generic method, e.g., OrderNotificationHandlers<MyNotification>
                var concreteOrderMethod = orderMethodInfo.MakeGenericMethod(notificationType);

                // Prepare arguments: IServiceCollection, Type[], ServiceLifetime
                object[] methodArgs = [services, specificHandlerTypes, options.ServiceLifetime];

                // Invoke the static method (instance is null)
                concreteOrderMethod.Invoke(null, methodArgs);
            }
            catch (Exception ex) when (ex is TargetInvocationException or ArgumentException)
            {
                // Catch reflection or argument errors specifically
                throw new InvalidOperationException($"Error registering ordered handlers for notification type '{notificationType.FullName}'. See inner exception.", ex);
            }
        }
    }

    private static void RegisterPipelineBehaviors(IServiceCollection services, IEnumerable<Type> behaviorTypes, SwitchMediatorOptions options)
    {
        foreach (var behaviorType in behaviorTypes)
        {
            services.TryAdd(new ServiceDescriptor(behaviorType, behaviorType, options.ServiceLifetime));
        }
    }

    internal static void OrderNotificationHandlers<TNotification>(this IServiceCollection services, Type[] handlerTypes, ServiceLifetime serviceLifetime)
        where TNotification : INotification
    {
        services.TryAdd(new ServiceDescriptor(
            typeof(IEnumerable<INotificationHandler<TNotification>>),
            sp => handlerTypes.Select(handlerType => GetNotificationHandler(sp, handlerType)),
            serviceLifetime));
        
        services.TryAdd(new ServiceDescriptor(
            typeof(IEnumerable<Lazy<INotificationHandler<TNotification>>>),
            sp => handlerTypes.Select(handlerType => 
                new Lazy<INotificationHandler<TNotification>>(() => GetNotificationHandler(sp, handlerType))),
            serviceLifetime));
        
        return;

        static INotificationHandler<TNotification> GetNotificationHandler(IServiceProvider sp, Type handlerType) => 
            (INotificationHandler<TNotification>) sp.GetRequiredService(handlerType);
    }

    private static Func<IServiceProvider, object> BuildLazyFactoryDelegate(Type serviceType)
    {
        // Expression Tree: sp => new Lazy<serviceType>(() => sp.GetRequiredService<serviceType>())

        // 1. Define IServiceProvider parameter
        var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");

        // 2. Get the MethodInfo for sp.GetRequiredService<T>
        var getRequiredServiceMethodInfo = typeof(ServiceProviderServiceExtensions).GetMethod(
            nameof(ServiceProviderServiceExtensions.GetRequiredService),
            [typeof(IServiceProvider)] // Specify overload taking IServiceProvider
        );

        if (getRequiredServiceMethodInfo == null)
            throw new InvalidOperationException("Could not find GetRequiredService method.");

        // 3. Make it generic for the specific serviceType: GetRequiredService<serviceType>
        var genericGetRequiredServiceMethod = getRequiredServiceMethodInfo.MakeGenericMethod(serviceType);

        // 4. Create the call expression: sp.GetRequiredService<serviceType>()
        var getServiceCall = Expression.Call(null, genericGetRequiredServiceMethod, spParam); // Static method call

        // 5. Create the inner lambda: () => sp.GetRequiredService<serviceType>()
        //    The delegate type Func<serviceType> is inferred.
        var innerLambda = Expression.Lambda(getServiceCall);

        // 6. Get the constructor for Lazy<serviceType>(Func<serviceType> factory)
        var lazyType = typeof(Lazy<>).MakeGenericType(serviceType);
        var lazyConstructor = lazyType.GetConstructor([innerLambda.Type]); // Get constructor matching Func<T>

        if (lazyConstructor == null)
            throw new InvalidOperationException($"Could not find constructor for Lazy<{serviceType.Name}> that accepts a factory delegate.");

        // 7. Create the 'new Lazy<serviceType>(innerLambda)' expression
        var lazyCreation = Expression.New(lazyConstructor, innerLambda);

        // 8. Create the outer lambda: sp => (object)new Lazy<serviceType>(...)
        //    The result needs to be object type for the ServiceDescriptor factory.
        var outerLambda = Expression.Lambda<Func<IServiceProvider, object>>(
            Expression.Convert(lazyCreation, typeof(object)), // Convert Lazy<T> to object
            spParam
        );

        // 9. Compile the expression tree into a delegate
        return outerLambda.Compile();
    }

    private static void Sort(Type[] typesToSort, Type[] specificOrder)
    {
        if (specificOrder.Length == 0)
        {
            return;
        }

        Array.Sort(typesToSort, (Comparison<Type>) Comparison);
        return;

        // note: This is going to be more performant than creating a dictionary for lookups given a small number of types
        int Comparison(Type x, Type y)
        {
            var indexX = Array.IndexOf(specificOrder, x);
            var indexY = Array.IndexOf(specificOrder, y);
            var keyX = indexX >= 0 ? indexX : specificOrder.Length;
            var keyY = indexY >= 0 ? indexY : specificOrder.Length;
            return keyX.CompareTo(keyY);
        }
    }
}