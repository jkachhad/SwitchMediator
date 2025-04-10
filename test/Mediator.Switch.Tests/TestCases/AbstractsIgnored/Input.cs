namespace Mediator.Switch.Tests.TestCases.AbstractsIgnored;

public class Ping : IRequest<string>;
public class PingHandler<TIgnoreMe> : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}
