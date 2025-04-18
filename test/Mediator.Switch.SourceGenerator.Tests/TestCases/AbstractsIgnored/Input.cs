using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.AbstractsIgnored;

public class Ping : IRequest<string>;
public class PingHandler<TIgnoreMe> : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}
