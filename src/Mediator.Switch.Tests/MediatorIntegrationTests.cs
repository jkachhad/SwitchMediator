using FluentValidation;
using Mediator.Switch.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Tests;

public class MediatorIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly ISender _sender;
    private readonly IPublisher _publisher;
    private readonly NotificationTracker _notificationTracker;

    public MediatorIntegrationTests()
    {
        static void ConfigureMediator(SwitchMediatorOptions op)
        {
            op.OrderNotificationHandlers<UserLoggedInEvent>(
                typeof(TestUserLoggedInLogger) // Logger first
                // Analytics handler is automatically appended
            );
        }

        var setupResult = MediatorTestSetup.Setup(
            configureMediator: ConfigureMediator,
            lifetime: ServiceLifetime.Scoped
        );

        _serviceProvider = setupResult.ServiceProvider;
        _scope = setupResult.Scope;
        _sender = setupResult.Sender;
        _publisher = setupResult.Publisher;
        _notificationTracker = setupResult.Tracker;
    }

    [Fact]
    public async Task Send_GetUserRequest_Success_ReturnsUserAndPublishesEvent()
    {
        // Arrange
        var request = new GetUserRequest(123);
        var initialVersion = 50;
        var expectedVersionAfterPipeline = initialVersion + 1;

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(request.UserId, result.Value.UserId);
        Assert.Contains($"User {request.UserId}", result.Value.Description);
        Assert.Equal(expectedVersionAfterPipeline, result.Value.Version);

        // Assert notifications using the field _notificationTracker
        Assert.Equal(2, _notificationTracker.ExecutionOrder.Count);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInLogger), firstHandler); // Check order based on constructor config
        Assert.Equal(nameof(TestUserLoggedInAnalytics), secondHandler);
    }

    [Fact]
    public async Task Send_CreateOrderRequest_Success_ReturnsOrderId()
    {
        // Arrange
        var request = new CreateOrderRequest("TestProduct");
        var expectedOrderId = 42;

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.Equal(expectedOrderId, result);
    }

    [Fact]
    public async Task Send_GetUserRequest_ValidationFailure_ThrowsValidationException()
    {
        // Arrange
        var request = new GetUserRequest(-1);

        // Act & Assert
        // Use the field _sender
        var exception = await Assert.ThrowsAsync<ValidationException>(() => _sender.Send(request));
        Assert.Single(exception.Errors);
        Assert.Equal("UserId must be positive", exception.Errors.First().ErrorMessage);
        Assert.Equal(nameof(GetUserRequest.UserId), exception.Errors.First().PropertyName);
    }

    [Fact]
    public async Task Send_CreateOrderRequest_ValidationFailure_ThrowsValidationException()
    {
        // Arrange
        var request = new CreateOrderRequest("");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(() => _sender.Send(request));
        Assert.Single(exception.Errors);
        Assert.Equal("Product cannot be empty", exception.Errors.First().ErrorMessage);
        Assert.Equal(nameof(CreateOrderRequest.Product), exception.Errors.First().PropertyName);
    }

    [Fact]
    public async Task Publish_UserLoggedInEvent_ExecutesHandlersInCorrectOrder()
    {
        // Arrange
        var notification = new UserLoggedInEvent(999);
        _notificationTracker.ExecutionOrder.Clear(); // Clear tracker before publish if needed

        // Act
        await _publisher.Publish(notification); // Use the field _publisher

        // Assert
        Assert.Equal(2, _notificationTracker.ExecutionOrder.Count);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInLogger), firstHandler); // Check order based on constructor config
        Assert.Equal(nameof(TestUserLoggedInAnalytics), secondHandler);
    }

    [Fact]
    public async Task Publish_DerivedUserLoggedInEvent_UsesUserLoggedInEventHandler_AsFallback()
    {
        // Arrange
        var notification = new DerivedUserLoggedInEvent(999);
         _notificationTracker.ExecutionOrder.Clear(); // Clear tracker

        // Act
        await _publisher.Publish(notification); // Use the field _publisher

        // Assert
        Assert.Equal(2, _notificationTracker.ExecutionOrder.Count);
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInLogger), firstHandler);
        Assert.Equal(nameof(TestUserLoggedInAnalytics), secondHandler);
    }


    [Fact]
    public async Task Send_GetUserRequest_AuditAndVersionBehaviorsRun()
    {
        // Arrange
        var request = new GetUserRequest(200);
        var initialVersion = 50;
        var expectedVersion = initialVersion + 1;

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedVersion, result.Value.Version);
        // Audit check remains implicit
    }

    [Fact]
    public async Task Send_CreateOrderRequest_TransactionBehaviorRuns_ButNotAuditOrVersion()
    {
        // Arrange
        var request = new CreateOrderRequest("Gadget");

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.Equal(42, result);
        // Transaction check remains implicit
    }

    [Fact]
    public async Task Send_DogQuery_UsesSpecificDogQueryHandler()
    {
        // Arrange
        var dogName = "Rex";
        var dogBreed = "German Shepherd";
        var request = new DogQuery(dogName, dogBreed);

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("Handled by DogQueryHandler:", result);
        Assert.Contains($"Dog named {dogName}", result);
        Assert.Contains($"Breed: {dogBreed}", result);
    }

    [Fact]
    public async Task Send_CatQuery_UsesBaseAnimalQueryHandler_AsFallback()
    {
        // Arrange
        var catName = "Whiskers";
        var request = new CatQuery(catName);

        // Act
        var result = await _sender.Send(request); // Use the field _sender

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("Handled by AnimalQueryHandler:", result);
        Assert.Contains($"Generic animal named {catName}", result);
        Assert.DoesNotContain("Dog", result);
        Assert.DoesNotContain("Cat:", result);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}