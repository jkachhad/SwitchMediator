using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Extensions.Microsoft.DependencyInjection;

public class SwitchMediatorOptions
{
	private readonly IServiceCollection _services;

	public SwitchMediatorOptions(IServiceCollection services) => 
		_services = services;

	/// <summary>
	/// The assemblies scanned for handlers and behaviors.
	/// </summary>
	public Assembly[] TargetAssemblies { get; set; } = [];
	
	/// <summary>
	/// The default lifetime for the services registered by the mediator.
	/// </summary>
	public ServiceLifetime ServiceLifetime { get; set; } = ServiceLifetime.Scoped;
	
	/// <summary>
	/// Specify explicit notification handler ordering.
	/// </summary>
	/// <param name="handlerTypes">Handler types in the preferred order.</param>
	/// <typeparam name="TNotification"></typeparam>
	/// <returns></returns>
	public SwitchMediatorOptions OrderNotificationHandlers<TNotification>(params Type[] handlerTypes)
		where TNotification : INotification
	{
		foreach (var handlerType in handlerTypes)
		{
			if (!typeof(INotificationHandler<TNotification>).IsAssignableFrom(handlerType))
			{
				throw new ArgumentException($"Type {handlerType.Name} does not implement INotificationHandler<{typeof(TNotification).Name}>");
			}
		}

		_services.OrderNotificationHandlers<TNotification>(handlerTypes, ServiceLifetime);
		return this;
	}
}