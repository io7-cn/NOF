---
description: Add domain classes, generated value objects, failures, and in-memory events to a NOF application
---

# Add Domain Types

NOF domain code uses ordinary classes plus generated value objects and failures. There is no framework aggregate-root or entity base class.

## 1. Add a Value Object

Value objects are `readonly partial struct` types implementing `IValueObject<T>`:

```csharp
using NOF.Domain;

[NewableValueObject]
public readonly partial struct OrderId : IValueObject<long>;
```

`[NewableValueObject]` is valid only for `IValueObject<long>` and generates `New()` plus `New(IIdGenerator)`.

For a normalized and validated string value object:

```csharp
using NOF.Domain;

[ValueObjectLength(200, MinimumLength = 3)]
public readonly partial struct EmailAddress : IValueObject<string>
{
    public static string Normalize(string value) => value.Trim().ToLowerInvariant();

    public static bool IsValid(string value, out string? errorMessage)
    {
        var valid = !string.IsNullOrWhiteSpace(value) && value.Contains('@');
        errorMessage = valid ? null : "Invalid value.";
        return valid;
    }
}
```

Construct values through `EmailAddress.From(...)` and extract the primitive with an explicit cast. `default(EmailAddress)` and `new EmailAddress()` are invalid (`NOF018`).

## 2. Declare Expected Application Failures in Contract

```csharp
using NOF.Contract;

public interface IOrderService : IApplicationService
{
    [FailureCase("NotFound", "404001")]
    [FailureCase("InvalidStatus", "400002")]
    Empty Confirm(ConfirmOrderRequest request);
}
```

The generator creates IOrderService.ConfirmOperation.Result and distinct case types. Domain
methods return their own business values; Application maps those values to the declared
cases. Error codes do not determine C# names. No static failure-holder class or
DomainException conversion is needed.

## 3. Add a Domain Class and Events

```csharp
using NOF.Abstraction;
using NOF.Domain;

public sealed class Order
{
    public OrderId Id { get; init; }
    public EmailAddress CustomerEmail { get; private set; }
    public string Status { get; private set; }

    private Order() { }

    public static Order Create(EmailAddress customerEmail)
    {
        var order = new Order
        {
            Id = OrderId.New(),
            CustomerEmail = customerEmail,
            Status = "Pending"
        };

        new OrderCreatedEvent(order.Id, order.CustomerEmail).PublishAsEvent();
        return order;
    }

    public bool TryConfirm()
    {
        if (Status == "Confirmed")
        {
            return false;
        }

        Status = "Confirmed";
        new OrderConfirmedEvent(Id).PublishAsEvent();
        return true;
    }
}

public sealed record OrderCreatedEvent(OrderId Id, EmailAddress CustomerEmail);
public sealed record OrderConfirmedEvent(OrderId Id);
```

`PublishAsEvent()` is synchronous convenience over the ambient publisher. Inside a NOF execution boundary it forwards the current `Context`. For explicit asynchronous control, inject `IEventPublisher` at the application boundary and call `PublishAsync(...)`.

## 4. Handle an Event

```csharp
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

public sealed class OrderCreatedProjectionHandler(IOrderProjectionStore projections)
    : InMemoryEventHandler<OrderCreatedEvent>
{
    public override Task HandleAsync(
        OrderCreatedEvent @event,
        Context context,
        CancellationToken cancellationToken)
    {
        projections.Add(OrderProjection.From(@event));
        return Task.CompletedTask;
    }
}
```

The handler is registered by its generated assembly initializer when the containing assembly is added with `AddApplicationPart(...)`.

## 5. Persist from the Application Layer

```csharp
public sealed class CreateOrder(IOrderRepository orders, IUnitOfWork unitOfWork) : OrderService.CreateOrder
{
    public override async Task<Empty> HandleAsync(
        CreateOrderRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        var order = Order.Create(EmailAddress.From(request.CustomerEmail));
        orders.Add(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Empty.Instance;
    }
}
```

Application code uses owned aggregate/query interfaces and IUnitOfWork. Concrete EF Core `DbContext` and `NOFDbContext` stay in the host project.
