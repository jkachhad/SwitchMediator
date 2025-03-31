using Mediator.Switch;
using System.Threading;
using System.Threading.Tasks;

namespace Test.GenericsIgnored;

public class Ping : IRequest<string>;
public class PingHandler<TIgnoreMe> : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}
