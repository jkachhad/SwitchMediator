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
	
	public SwitchMediatorOptions OrderNotificationHandlers<TNotification>(params Type[] handlerTypes)
		where TNotification : INotification
	{
		_services.Add(new ServiceDescriptor(typeof(IEnumerable<Lazy<INotificationHandler<TNotification>>>),
			sp => handlerTypes
				.Select(handlerType => new Lazy<INotificationHandler<TNotification>>(() => (INotificationHandler<TNotification>) sp.GetRequiredService(handlerType)))
				.ToList(),
			ServiceLifetime));
		
		return this;
	}
}