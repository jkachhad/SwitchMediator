using Mediator.Switch;
using System.Threading.Tasks;

namespace Test.BasicPipelineNestedType;

public interface IResult<out T>
{
    T Value { get; }
}

public class Result<T> : IResult<T>
{
    public T Value { get; set; }
    public Result(T value)
    {
        Value = value;
    }
}

public interface IVersionedResponse
{
    int Version { get; }
}

public class VersionedResponse : IVersionedResponse
{
    public int Version { get; set; }
}

public class Ping : IRequest<Result<VersionedResponse>>;

public class PingHandler : IRequestHandler<Ping, Result<VersionedResponse>>
{
    public Task<Result<VersionedResponse>> Handle(Ping request) => Task.FromResult(new Result<VersionedResponse>(new VersionedResponse { Version = 42 }));
}

public class GenericBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IResult<IVersionedResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next)
    {
        return await next();
    }
}