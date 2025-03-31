## SwitchMediator Sample Console Application

This sample application demonstrates various technical capabilities of the **SwitchMediator** library:

*   **Request/Response Handling:** Shows the basic pattern using `IRequest<TResponse>` (e.g., `GetUserRequest`, `CreateOrderRequest`) and their corresponding `IRequestHandler` implementations.

*   **Publish/Subscribe Notifications:** Demonstrates broadcasting events (`UserLoggedInEvent` implementing `INotification`) to multiple handlers (`UserNotificationLogger`, `UserNotificationAnalytics` implementing `INotificationHandler`).

*   **Handler Discovery via Attribute:** Uses the `[RequestHandler]` attribute on request types (`GetUserRequest`, `CreateOrderRequest`) to link them to their specific handler implementation, decoupling the request definition from direct handler knowledge.

*   **Pipeline Behaviors (`IPipelineBehavior`):** Illustrates the middleware concept for requests:
    *   **Generic Behaviors:** Applied to all requests matching constraints (e.g., `LoggingBehavior`).
    *   **Constrained Behaviors:** Applied only to requests implementing specific marker interfaces (e.g., `AuditBehavior` requires `IAuditableRequest`, `TransactionBehavior` requires `ITransactionalRequest`).
    *   **Explicit Ordering:** Uses `[PipelineBehaviorOrder(N)]` to control the execution sequence of behaviors. Behaviors without the attribute run last (default order `int.MaxValue`).
    *   **Dependency Injection:** Behaviors can receive dependencies (e.g., `ValidationBehavior` receiving `IValidator<TRequest>`).

*   **FluentValidation Integration:** The `ValidationBehavior` seamlessly integrates with FluentValidation by resolving `IValidator<TRequest>` from the DI container and executing validation logic within the pipeline. Shows automatic registration via `AddValidatorsFromAssembly`.

*   **FluentResults Integration & Response Adaptation:**
    *   Requests can return wrapped results like `FluentResults.Result<T>` (e.g., `GetUserRequest`).
    *   Behaviors can intelligently work with these wrapped types. `VersionIncrementingBehavior` uses `[PipelineBehaviorResponseAdaptor(typeof(Result<>))]` to correctly handle the `Result<User>` response, access the inner `User` object (constrained via `where TResponse : IVersionedResponse`), modify it, and potentially return a failed `Result`.

*   **Dependency Injection Setup:** Shows configuration using `SwitchMediator.Extensions.Microsoft.DependencyInjection` (`AddScoped<SwitchMediator>`), including assembly scanning for automatic registration of handlers and behaviors.

*   **Notification Handler Ordering:** Demonstrates explicit control over the execution order for handlers of a specific notification type using `OrderNotificationHandlers<TNotification>(...)`.

*   **Core Abstractions:** Uses the `ISender` and `IPublisher` interfaces to interact with the mediator.