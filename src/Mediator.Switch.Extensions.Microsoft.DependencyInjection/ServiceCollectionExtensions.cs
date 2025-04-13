using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Extensions.Microsoft.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediator<TSwitchMediator>(this IServiceCollection services, ServiceLifetime serviceLifetime, params Assembly[] assembliesToScan)
        where TSwitchMediator : class, IMediator
    {
        return AddMediator<TSwitchMediator>(services, op =>
        {
            op.TargetAssemblies = assembliesToScan;
            op.ServiceLifetime = serviceLifetime;
        });
    }

    public static IServiceCollection AddMediator<TSwitchMediator>(this IServiceCollection services, Action<SwitchMediatorOptions>? configure)
        where TSwitchMediator : class, IMediator
    {
        var options = new SwitchMediatorOptions(services);

        configure?.Invoke(options);

        services.Add(new ServiceDescriptor(typeof(IMediator), typeof(TSwitchMediator), options.ServiceLifetime));
        services.Add(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<IMediator>(), options.ServiceLifetime));
        services.Add(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<IMediator>(), options.ServiceLifetime));

        // get all types from the target assemblies
        var allTypes = options.TargetAssemblies.Where(assembly => assembly != null)
            .SelectMany(assembly => assembly.GetTypes())
            .ToArray();

        // Register Handlers
        var handlerTypes = allTypes
            .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
            .ToList();

        foreach (var handlerType in handlerTypes)
        {
            services.Add(new ServiceDescriptor(handlerType, handlerType, options.ServiceLifetime));
            services.Add(new ServiceDescriptor(typeof(Lazy<>).MakeGenericType(handlerType),
                sp => CreateLazyService(sp, handlerType), options.ServiceLifetime));
        }

        // Register Notification Handlers (without explicit ordering initially)
        var notificationHandlerTypes = allTypes
            .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
            .ToList();

        foreach (var handlerType in notificationHandlerTypes)
        {
            // Register the concrete type
            services.Add(new ServiceDescriptor(handlerType, handlerType, options.ServiceLifetime));
            services.Add(new ServiceDescriptor(typeof(Lazy<>).MakeGenericType(handlerType),
                sp => CreateLazyService(sp, handlerType), options.ServiceLifetime));

            // TODO
            // Also register against notification handler interfaces
            // foreach (var handlerInterface in handlerType.GetInterfaces()
            //              .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
            // {
            //     services.Add(new ServiceDescriptor(handlerInterface, sp => sp.GetRequiredService(handlerType), options.ServiceLifetime));
            // }
        }

        // Register Pipeline Behaviors
        var pipelineBehaviorTypes = allTypes
            .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
            .ToList();

        foreach (var behaviorType in pipelineBehaviorTypes)
        {
            services.Add(new ServiceDescriptor(behaviorType, behaviorType, options.ServiceLifetime));
        }

        return services;
    }

    private static object CreateLazyService(IServiceProvider serviceProvider, Type type)
    {
        var lazyType = typeof(Lazy<>).MakeGenericType(type);
        var funcType = typeof(Func<>).MakeGenericType(type);

        // We need to create a Func<T> delegate dynamically that resolves the service.
        // Expression trees are a good way to do this: () => serviceProvider.GetRequiredService<T>()

        // 1. Get the MethodInfo for serviceProvider.GetRequiredService<T>()
        // Note: GetRequiredService<T> is an extension method in ServiceProviderServiceExtensions
        var getServiceMethod = typeof(ServiceProviderServiceExtensions)
            .GetMethod(nameof(ServiceProviderServiceExtensions.GetRequiredService),
                [typeof(IServiceProvider)])?
            .MakeGenericMethod(type);

        if (getServiceMethod == null) // Should not happen in standard scenarios
        {
            throw new InvalidOperationException(
                $"Unable to find the GetRequiredService method for type {type.Name}.");
        }

        // 2. Build the expression tree
        // Constant expression representing the serviceProvider instance passed to our factory
        var providerConstant = Expression.Constant(serviceProvider);
        // Call expression representing serviceProvider.GetRequiredService<handlerType>()
        var callExpression =
            Expression.Call(null, getServiceMethod, providerConstant); // It's a static extension method
        // Lambda expression representing the Func<handlerType>
        var lambdaExpression = Expression.Lambda(funcType, callExpression);

        // 3. Compile the expression tree into an actual Func<T> delegate
        var funcDelegate = lambdaExpression.Compile();

        // 4. Create the Lazy<handlerType> instance using its constructor that takes Func<T>
        // Use Activator.CreateInstance as we don't know the type at compile time.
        return Activator.CreateInstance(lazyType, funcDelegate)!;
    }
}