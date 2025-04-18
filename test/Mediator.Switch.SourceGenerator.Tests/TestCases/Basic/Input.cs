using Mediator.Switch;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.Basic;

[RequestHandler(typeof(PingHandler))]
public class Ping : IRequest<string>;
public class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken = default) => Task.FromResult("Pong");
}
