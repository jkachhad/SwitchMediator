using FluentValidation;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Tests;

public abstract class MediatorTestBase : IDisposable
{
    protected readonly ISender Sender;
    protected readonly IPublisher Publisher;
    protected readonly IServiceScope Scope;

    private readonly IServiceProvider _serviceProvider;

    protected MediatorTestBase()
    {
        var services = new ServiceCollection();

        services.AddValidatorsFromAssembly(typeof(MediatorTestBase).Assembly, includeInternalTypes: true);

        // Register Test Notification Handlers instead of console ones
        services.AddSingleton<NotificationTracker>(); // Add tracker

        // Register SwitchMediator - Mirroring Program.cs setup
        services.AddMediator<SwitchMediator>(op =>
        {
            op.KnownTypes = SwitchMediator.KnownTypes;
            op.ServiceLifetime = ServiceLifetime.Scoped;

            op.OrderNotificationHandlers<UserLoggedInEvent>(
                typeof(TestUserLoggedInLogger) // Logger first
                // typeof(TestUserLoggedInAnalytics) // Analytics second (should be automatically registered)
            );
        });

        _serviceProvider = services.BuildServiceProvider();
        Scope = _serviceProvider.CreateScope();

        Sender = Scope.ServiceProvider.GetRequiredService<ISender>();
        Publisher = Scope.ServiceProvider.GetRequiredService<IPublisher>();
    }

    public void Dispose()
    {
        Scope.Dispose();
        (_serviceProvider as ServiceProvider)?.Dispose();
        GC.SuppressFinalize(this);
    }
}