namespace Mediator.Switch;

public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public interface IPublisher
{
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
}

public interface IMediator : ISender, IPublisher;

public interface IRequest<out TResponse>;

public interface INotification;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}

public interface INotificationHandler<in TNotification>
{
    Task Handle(TNotification notification, CancellationToken cancellationToken = default);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : notnull
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default);
}
