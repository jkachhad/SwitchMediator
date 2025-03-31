using Mediator.Switch;
using System.Threading;
using System.Threading.Tasks;

namespace Test.AbstractsIgnored;

public class Ping : IRequest<string>;
public abstract class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}
