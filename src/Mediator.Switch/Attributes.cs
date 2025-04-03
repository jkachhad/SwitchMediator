namespace Mediator.Switch;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipelineBehaviorOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipelineBehaviorResponseAdaptorAttribute(Type genericsType) : Attribute
{
    public Type GenericsType { get; } = genericsType;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequestHandlerAttribute(Type handlerType) : Attribute
{
    public Type HandlerType { get; } = handlerType;
}
