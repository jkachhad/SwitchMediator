using BenchmarkDotNet.Attributes;
using Mediator.Switch.Benchmark.Generated;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Benchmark;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class MediatorBenchmarks
{
    [Params(25, 200, 1000)]
    public int N; // Represents the number of parallel handlers compiled

    [Params(0, 1, 5)]
    public int B; // Number of open behaviors

    private IServiceProvider _mediatRProvider = null!;
    private IServiceProvider _switchMediatorProvider = null!;

    // Separate instances for each library
    private Ping1Request_MediatR _requestToSendMediatR = null!;
    private Notify1Event_MediatR _notificationToPublishMediatR = null!;
    private Ping1Request_Switch _requestToSendSwitch = null!;
    private Notify1Event_Switch _notificationToPublishSwitch = null!;

    private const string TargetNamespace = "Mediator.Switch.Benchmark.Generated";

    [GlobalSetup]
    public void GlobalSetup()
    {
        Console.WriteLine($"// GlobalSetup running for N={N}, BehaviorCount={B}");
        // Assembly contains BOTH _MediatR and _Switch types for the current N
        var handlerAssembly = typeof(Ping1RequestHandler_MediatR).Assembly; // Or _Switch

        // --- MediatR Setup ---
        var mediatRServices = new ServiceCollection();
        mediatRServices.AddMediatR(cfg => {
            // MediatR scans and finds types implementing its interfaces (_MediatR types)
            cfg.RegisterServicesFromAssembly(handlerAssembly);
            cfg.Lifetime = ServiceLifetime.Singleton;
            // Register open behaviors
            for (var i = 1; i <= B; i++)
            {
                cfg.AddOpenBehavior(handlerAssembly.GetType($"{TargetNamespace}.OpenBehavior{i}_MediatR`2") ??
                                    throw new InvalidOperationException());
            }
        });
        _mediatRProvider = mediatRServices.BuildServiceProvider();

        // --- SwitchMediator Setup ---
        var switchMediatorServices = new ServiceCollection();
        switchMediatorServices.AddMediator<SwitchMediator>(op => // Use actual generated type
        {
            // Your Source Generator should populate KnownTypes ONLY with types
            // implementing Mediator.Switch interfaces (_Switch types).
            op.KnownTypes = SwitchMediator.KnownTypes;
            op.ServiceLifetime = ServiceLifetime.Singleton;
        });
        // AddMediator should have registered all _Switch handlers/behaviors into DI.
        _switchMediatorProvider = switchMediatorServices.BuildServiceProvider();

        // --- Prepare instances ---
        _requestToSendMediatR = new Ping1Request_MediatR { Id = 1 };
        _notificationToPublishMediatR = new Notify1Event_MediatR { Message = "Benchmark MediatR" };
        _requestToSendSwitch = new Ping1Request_Switch { Id = 1 };
        _notificationToPublishSwitch = new Notify1Event_Switch { Message = "Benchmark Switch" };

        // --- Sanity Check ---
        ValidateMediatR(_mediatRProvider);
        ValidateSwitchMediator(_switchMediatorProvider);
        Console.WriteLine($"// GlobalSetup complete for N={N}");
    }

    // --- Startup Benchmarks ---
    [Benchmark(Description = "MediatR: Build Service Provider")]
    public IServiceProvider MediatR_Startup()
    {
        var services = new ServiceCollection();
        var handlerAssembly = typeof(Ping1RequestHandler_MediatR).Assembly;
        services.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssembly(handlerAssembly);
            cfg.Lifetime = ServiceLifetime.Singleton;
        });
        var sp = services.BuildServiceProvider(); // Measures scanning + DI registration
        sp.Dispose(); // Teardown should be negligible but prevents garbage build up
        return sp;
    }

    [Benchmark(Description = "SwitchMediator: Build Service Provider")]
    public IServiceProvider SwitchMediator_Startup()
    {
        var services = new ServiceCollection();
        services.AddMediator<SwitchMediator>(op =>
        {
            op.KnownTypes = SwitchMediator.KnownTypes;
            op.ServiceLifetime = ServiceLifetime.Singleton;
        });
        // Measures DI registration performed by AddMediator based on KnownTypes
        var sp = services.BuildServiceProvider();
        sp.Dispose(); // Teardown should be negligible but prevents garbage build up
        return sp;
    }

    // --- Execution Benchmarks ---
    [Benchmark(Description = "MediatR: Send Request")]
    public Task<Pong1Response_MediatR> MediatR_Send()
    {
        var mediator = _mediatRProvider.GetRequiredService<MediatR.ISender>();
        return mediator.Send(_requestToSendMediatR); // Use MediatR request type
    }

    [Benchmark(Description = "SwitchMediator: Send Request")]
    public Task<Pong1Response_Switch> SwitchMediator_Send()
    {
        var mediator = _switchMediatorProvider.GetRequiredService<ISender>();
        return mediator.Send(_requestToSendSwitch); // Use Switch request type
    }

    [Benchmark(Description = "MediatR: Publish Notification")]
    public Task MediatR_Publish()
    {
        var mediator = _mediatRProvider.GetRequiredService<MediatR.IPublisher>();
        return mediator.Publish(_notificationToPublishMediatR); // Use MediatR notification
    }

    [Benchmark(Description = "SwitchMediator: Publish Notification")]
    public Task SwitchMediator_Publish()
    {
        var mediator = _switchMediatorProvider.GetRequiredService<IPublisher>();
        return mediator.Publish(_notificationToPublishSwitch); // Use Switch notification
    }

    // --- Validation Helpers ---
    private void ValidateMediatR(IServiceProvider provider)
    {
        const string name = "MediatR";
        try {
            var sender = provider.GetRequiredService<MediatR.ISender>();
            var publisher = provider.GetRequiredService<MediatR.IPublisher>();
            var sendTask = sender.Send(_requestToSendMediatR);
            _ = sendTask.GetAwaiter().GetResult();
            var publishTask = publisher.Publish(_notificationToPublishMediatR);
            publishTask.GetAwaiter().GetResult();
            Console.WriteLine($"// Validation PASSED for {name}");
        } catch (Exception ex) {
            Console.WriteLine($"// !!! Validation FAILED for {name}: {ex.Message} {ex.StackTrace}");
            throw new InvalidOperationException($"Validation failed for {name}", ex);
        }
    }
    private void ValidateSwitchMediator(IServiceProvider provider)
    {
        const string name = "SwitchMediator";
        try {
            var sender = provider.GetRequiredService<ISender>();
            var publisher = provider.GetRequiredService<IPublisher>();
            var sendTask = sender.Send(_requestToSendSwitch);
            _ = sendTask.GetAwaiter().GetResult();
            var publishTask = publisher.Publish(_notificationToPublishSwitch);
            publishTask.GetAwaiter().GetResult();
            Console.WriteLine($"// Validation PASSED for {name}");
        } catch (Exception ex) {
            Console.WriteLine($"// !!! Validation FAILED for {name}: {ex.Message} {ex.StackTrace}");
            throw new InvalidOperationException($"Validation failed for {name}", ex);
        }
    }
}

// --- Program.cs ---
public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Starting Mediator Benchmarks...");
        // This will run benchmarks based on the compiled code,
        // respecting the --filter argument passed by the automation script.
        BenchmarkDotNet.Running.BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        Console.WriteLine("Mediator Benchmarks finished.");
    }
}