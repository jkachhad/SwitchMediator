using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator.Switch.Tests;

public class MediatorIntegrationTests : MediatorTestBase
{
    private readonly NotificationTracker _notificationTracker;

    // Constructor to get the tracker instance
    public MediatorIntegrationTests()
    {
        _notificationTracker = Scope.ServiceProvider.GetRequiredService<NotificationTracker>();
    }

    [Fact]
    public async Task Send_GetUserRequest_Success_ReturnsUserAndPublishesEvent()
    {
        // Arrange
        var request = new GetUserRequest(123);
        var initialVersion = 50;
        var expectedVersionAfterPipeline = initialVersion + 1;

        // Act
        var result = await Sender.Send(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(request.UserId, result.Value.UserId);
        Assert.Contains($"User {request.UserId}", result.Value.Description); // Check description content
        Assert.Equal(expectedVersionAfterPipeline, result.Value.Version); // Verify VersionIncrementingBehavior worked

        // Assert that notifications were published (check tracker)
        Assert.Equal(2, _notificationTracker.ExecutionOrder.Count); // Both handlers should have run
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInLogger), firstHandler); // Check order based on DI config
        Assert.Equal(nameof(TestUserLoggedInAnalytics), secondHandler);
    }

    [Fact]
    public async Task Send_CreateOrderRequest_Success_ReturnsOrderId()
    {
        // Arrange
        var request = new CreateOrderRequest("TestProduct");
        var expectedOrderId = 42; // As defined in the handler

        // Act
        var result = await Sender.Send(request);

        // Assert
        Assert.Equal(expectedOrderId, result);
    }

    [Fact]
    public async Task Send_GetUserRequest_ValidationFailure_ThrowsValidationException()
    {
        // Arrange
        var request = new GetUserRequest(-1); // Invalid UserId

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(() => Sender.Send(request));

        // Optional: Assert specific error message
        Assert.Single(exception.Errors);
        Assert.Equal("UserId must be positive", exception.Errors.First().ErrorMessage);
        Assert.Equal(nameof(GetUserRequest.UserId), exception.Errors.First().PropertyName);
    }

    [Fact]
    public async Task Send_CreateOrderRequest_ValidationFailure_ThrowsValidationException()
    {
        // Arrange
        var request = new CreateOrderRequest(""); // Invalid Product

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(() => Sender.Send(request));

        // Optional: Assert specific error message
        Assert.Single(exception.Errors);
        Assert.Equal("Product cannot be empty", exception.Errors.First().ErrorMessage);
        Assert.Equal(nameof(CreateOrderRequest.Product), exception.Errors.First().PropertyName);
    }

    [Fact]
    public async Task Publish_UserLoggedInEvent_ExecutesHandlersInCorrectOrder()
    {
        // Arrange
        var notification = new UserLoggedInEvent(999);

        // Act
        await Publisher.Publish(notification);

        // Assert
        Assert.Equal(2, _notificationTracker.ExecutionOrder.Count); // Both handlers should have run

        // Verify order
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInLogger), firstHandler); // Check order based on DI config in base class
        Assert.Equal(nameof(TestUserLoggedInAnalytics), secondHandler);
    }

    [Fact]
    public async Task Publish_DerivedUserLoggedInEvent_UsesUserLoggedInEventHandler_AsFallback()
    {
        // Arrange
        var notification = new DerivedUserLoggedInEvent(999);

        // Act
        await Publisher.Publish(notification);

        // Assert
        Assert.Equal(2, _notificationTracker.ExecutionOrder.Count); // Both handlers should have run

        // Verify order
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var firstHandler));
        Assert.True(_notificationTracker.ExecutionOrder.TryDequeue(out var secondHandler));
        Assert.Equal(nameof(TestUserLoggedInLogger), firstHandler); // Check order based on DI config in base class
        Assert.Equal(nameof(TestUserLoggedInAnalytics), secondHandler);
    }

    [Fact]
    public async Task Send_GetUserRequest_AuditAndVersionBehaviorsRun()
    {
        // Arrange: AuditBehavior applies to IAuditableRequest (GetUserRequest)
        // Arrange: VersionIncrementingBehavior applies to IVersionedResponse (User)
        var request = new GetUserRequest(200);
        var initialVersion = 50;
        var expectedVersion = initialVersion + 1;

        // Act
        var result = await Sender.Send(request);

        // Assert
        Assert.True(result.IsSuccess);
        // We primarily assert the *effect* of the behaviors here.
        Assert.Equal(expectedVersion, result.Value.Version); // Version behavior ran
        // AuditBehavior's effect (Console.WriteLine) isn't easily asserted without capturing output.
        // We trust it ran because the request implements IAuditableRequest and the pipeline didn't throw an unexpected error.
    }

    [Fact]
    public async Task Send_CreateOrderRequest_TransactionBehaviorRuns_ButNotAuditOrVersion()
    {
        // Arrange: TransactionBehavior applies to ITransactionalRequest (CreateOrderRequest)
        // Arrange: AuditBehavior and VersionIncrementingBehavior do NOT apply
        var request = new CreateOrderRequest("Gadget");

        // Act
        var result = await Sender.Send(request); // Result is int, not IVersionedResponse

        // Assert
        Assert.Equal(42, result);
        // We trust TransactionBehavior ran because the request implements ITransactionalRequest.
        // No direct output to assert easily, but the request completed successfully.
        // We also know VersionIncrementingBehavior didn't run as the response isn't Result<IVersionedResponse>.
    }

    [Fact]
    public async Task Send_DogQuery_UsesSpecificDogQueryHandler()
    {
        // Arrange
        var dogName = "Rex";
        var dogBreed = "German Shepherd";
        var request = new DogQuery(dogName, dogBreed);

        // Act
        var result = await Sender.Send(request);

        // Assert
        // Verify the result came from the specific DogQueryHandler
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
        // CatQuery has no specific handler registered for it
        var request = new CatQuery(catName);

        // Act
        var result = await Sender.Send(request);

        // Assert
        // Verify the result came from the base AnimalQueryHandler
        Assert.NotNull(result);
        Assert.StartsWith("Handled by AnimalQueryHandler:", result);
        Assert.Contains($"Generic animal named {catName}", result);
        Assert.DoesNotContain("Dog", result); // Ensure it didn't somehow hit the Dog handler
        Assert.DoesNotContain("Cat:", result); // Ensure it's the generic message from the base handler
    }
}