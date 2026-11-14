using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Security.Claims;
using Xunit;

namespace NOF.Infrastructure.Tests.Middlewares;

public sealed class AuthorizationInvocationFilterTests
{
    [Theory]
    [InlineData(InvocationOrigin.Http)]
    [InlineData(InvocationOrigin.Local)]
    [InlineData(InvocationOrigin.Message)]
    public async Task InvokeAsync_UninitializedAuthorizationResult_ShouldNeverExecuteHandler(InvocationOrigin kind)
    {
        var middleware = CreateMiddleware(new UserContext(), new UninitializedAuthorizationHandler());
        var nextCalled = false;

        async Task InvokeAsync()
        {
            var invocation = CreateRequestContext(nameof(TestService.AllowAnonymousMethod), origin: kind);
            await middleware.InvokeAsync(invocation, new TestRequest(), (_, _, _) =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(Empty.Instance);
            }, default);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(InvokeAsync);
        Assert.Contains("uninitialized", exception.Message);
        Assert.False(nextCalled);
    }

    public sealed class UninitializedAuthorizationHandler : IInboundAuthorizationHandler
    {
        public ValueTask<InboundAuthorizationResult> AuthorizeAsync(InboundAuthorizationContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(default(InboundAuthorizationResult));
    }

    [Theory]
    [InlineData(null, "forged", "Permission denied")]
    [InlineData("host", "forged", "Permission denied")]
    [InlineData("tenantb", "tenanta", "Permission denied")]
    [InlineData("tenantb", "tenantb", null)]
    [InlineData("tenantb", "host", null)]
    public async Task InvokeAsync_StrictTenantPolicy_ShouldValidateBeforeHandlerResolution(
        string? identityTenant, string requestTenant, string? title)
    {
        var userContext = new UserContext();
        var identity = CreateAuthenticatedIdentity();
        if (identityTenant is not null)
        {
            identity.AddClaim(new Claim(ClaimTypes.TenantId, identityTenant));
        }
        userContext.User.AddIdentity(identity);
        var middleware = CreateMiddleware(userContext, tenantOptions: new()
        {
            RequireAuthenticatedTenant = true,
            RejectMismatchedTenant = true
        });
        var context = (InvocationContext)CreateRequestContext(nameof(TestService.LoginOnlyMethod))
            .WithTenantId(requestTenant);
        string? forwardedTenant = null;

        var outcome = await middleware.InvokeAsync(context, new TestRequest(), (forwarded, _, _) =>
        {
            forwardedTenant = forwarded.TenantId;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);

        if (title is not null)
        {
            Assert.Null(forwardedTenant);
            Assert.Equal(title, AsFailure(outcome).Title);
        }
        else
        {
            Assert.Equal(identityTenant, forwardedTenant);
        }
    }

    [Fact]
    public async Task InvokeAsync_StrictTenantPolicy_ShouldRequireAuthenticationOnAnonymousMethods()
    {
        var middleware = CreateMiddleware(new UserContext(), tenantOptions: new()
        {
            RequireAuthenticatedTenant = true
        });
        var context = CreateRequestContext(nameof(TestService.AllowAnonymousMethod));
        var nextCalled = false;
        var outcome = await middleware.InvokeAsync(context, new TestRequest(), (_, _, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);

        Assert.False(nextCalled);
        Assert.Equal("Authentication required", AsFailure(outcome).Title);
    }

    [Fact]
    public async Task InvokeAsync_StrictTenantPolicy_HostOperationStillRequiresPlatformPermission()
    {
        var userContext = new UserContext();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity());
        var middleware = CreateMiddleware(userContext, tenantOptions: new()
        {
            RequireAuthenticatedTenant = true,
            RejectMismatchedTenant = true
        });
        var deniedContext = CreateRequestContext(nameof(TestService.HostMethod));
        var deniedOutcome = await middleware.InvokeAsync(deniedContext, new TestRequest(), (_, _, _) =>
            throw new InvalidOperationException("An unauthorized host handler must not run"), default);
        Assert.Equal("Permission denied", AsFailure(deniedOutcome).Title);

        userContext.Logout();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity(ClaimTypes.Permission, "platform.manage"));
        var nextCalled = false;
        await middleware.InvokeAsync(CreateRequestContext(nameof(TestService.HostMethod)), new TestRequest(), (_, _, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_StrictRequestPolicy_DoesNotChangeTrustedMessageTenantHandling()
    {
        var middleware = CreateMiddleware(new UserContext(), tenantOptions: new()
        {
            RequireAuthenticatedTenant = true,
            RejectMismatchedTenant = true
        });
        var context = (InvocationContext)CreateCommandContext(
            typeof(TestRequest), typeof(TestHandler), nameof(TestHandler.AllowAnonymousHandler)).WithTenantId("internaltenant");
        string? forwardedTenant = null;
        var outcome = await middleware.InvokeAsync(context, new TestRequest(), (forwarded, _, _) =>
        {
            forwardedTenant = forwarded.TenantId;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);
        Assert.Null(forwardedTenant);
        Assert.Equal("Authentication required", AsFailure(outcome).Title);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUserWithoutTenant_PreservesUntrustedContextTenant()
    {
        var userContext = new UserContext();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity());
        var middleware = CreateMiddleware(userContext);
        var context = (InvocationContext)CreateRequestContext(
            nameof(TestService.LoginOnlyMethod)).WithTenantId("untrusted");

        string? forwardedTenant = null;
        var outcome = await middleware.InvokeAsync(context, new TestRequest(), (forwarded, _, _) =>
        {
            forwardedTenant = forwarded.TenantId;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);

        // Characterizes the default. Public hosts must add tenant authorization.
        Assert.Equal("untrusted", forwardedTenant);
    }

    [Fact]
    public async Task InvokeAsync_MethodAllowAnonymous_ShouldOverrideClassPermission_AndAllowUnauthenticated()
    {
        var userContext = new UserContext();
        var middleware = CreateMiddleware(userContext);
        var request = new TestRequest();

        var nextCalled = false;
        await middleware.InvokeAsync(CreateRequestContext(nameof(TestService.AllowAnonymousMethod)), request, (_, _, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_EmptyPermission_ShouldRequireAuthentication_AndSetAuthenticateHeader()
    {
        var userContext = new UserContext();
        var middleware = CreateMiddleware(userContext);

        var context = CreateRequestContext(nameof(TestService.LoginOnlyMethod));
        var outcome = await middleware.InvokeAsync(context, new TestRequest(), (_, _, _) => ValueTask.FromResult<object?>(Empty.Instance), default);

        var response = AsFailure(outcome);
        Assert.NotNull(response);
        Assert.Equal("Authentication required", response.Title);
        Assert.Equal("Please login first", response.Detail);
    }

    [Fact]
    public async Task InvokeAsync_RequestInputMethodPermission_ShouldOverrideInputTypePermission()
    {
        var userContext = new UserContext();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity(ClaimTypes.Permission, "input-type"));
        var middleware = CreateMiddleware(userContext);

        var deniedContext = CreateRequestContext(nameof(TestService.OverridePermissionMethod), handlerMethodName: nameof(TestHandler.AllowAnonymousHandler));
        var deniedOutcome = await middleware.InvokeAsync(deniedContext, new TestRequest(), (_, _, _) => ValueTask.FromResult<object?>(Empty.Instance), default);

        var denied = AsFailure(deniedOutcome);
        Assert.Equal("Permission denied", denied.Title);

        userContext.Logout();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity(ClaimTypes.Permission, "input-method"));
        var allowedContext = CreateRequestContext(nameof(TestService.OverridePermissionMethod), handlerMethodName: nameof(TestHandler.AllowAnonymousHandler));
        var nextCalled = false;
        await middleware.InvokeAsync(allowedContext, new TestRequest(), (_, _, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_Request_ShouldRequireInputAndExecutionPermissions()
    {
        var userContext = new UserContext();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity(ClaimTypes.Permission, "input-method"));
        var middleware = CreateMiddleware(userContext);

        var deniedContext = CreateRequestContext(nameof(TestService.OverridePermissionMethod), handlerMethodName: nameof(TestHandler.ExecutePermissionHandler));
        var deniedOutcome = await middleware.InvokeAsync(deniedContext, new TestRequest(), (_, _, _) => ValueTask.FromResult<object?>(Empty.Instance), default);

        var denied = AsFailure(deniedOutcome);
        Assert.Equal("Permission denied", denied.Title);

        userContext.Logout();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity(
            ClaimTypes.Permission, "input-method",
            ClaimTypes.Permission, "execute-method"));
        var allowedContext = CreateRequestContext(nameof(TestService.OverridePermissionMethod), handlerMethodName: nameof(TestHandler.ExecutePermissionHandler));
        var nextCalled = false;
        await middleware.InvokeAsync(allowedContext, new TestRequest(), (_, _, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_DefaultHandler_ShouldNotTreatScopeAsPermission()
    {
        var userContext = new UserContext();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity("scope", "input-method"));
        var middleware = CreateMiddleware(userContext);

        var context = CreateRequestContext(nameof(TestService.OverridePermissionMethod), handlerMethodName: nameof(TestHandler.AllowAnonymousHandler));
        var outcome = await middleware.InvokeAsync(
            context,
            new TestRequest(),
            (_, _, _) => ValueTask.FromResult<object?>(Empty.Instance),
            default);

        var denied = AsFailure(outcome);
        Assert.Equal("Permission denied", denied.Title);
    }

    [Fact]
    public async Task InvokeAsync_WhenAuthenticatedUserHasTenant_ShouldOverrideContextTenant()
    {
        var userContext = new UserContext();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity(
            ClaimTypes.Permission, "input-method",
            ClaimTypes.TenantId, "tenantb"));
        var middleware = CreateMiddleware(userContext);
        var context = (InvocationContext)CreateRequestContext(
            nameof(TestService.OverridePermissionMethod),
            handlerMethodName: nameof(TestHandler.AllowAnonymousHandler)).WithTenantId("tenanta");

        var tenantDuringNext = string.Empty;
        var outcome = await middleware.InvokeAsync(context, new TestRequest(), (forwardedContext, _, _) =>
        {
            tenantDuringNext = forwardedContext.TenantId;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);

        Assert.Equal("tenantb", tenantDuringNext);
        Assert.Equal("tenanta", context.TenantId);
    }

    [Fact]
    public async Task InvokeAsync_Command_ShouldRequireMessageAndHandlerPermissions()
    {
        var userContext = new UserContext();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity(ClaimTypes.Permission, "message"));
        var middleware = CreateMiddleware(userContext);
        var context = CreateCommandContext(typeof(ProtectedCommand), typeof(CommandHandlerWithPermission), nameof(CommandHandlerWithPermission.Handle));

        var nextCalled = false;
        var outcome = await middleware.InvokeAsync(context, new ProtectedCommand(), (_, _, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);
        Assert.False(nextCalled);

        userContext.Logout();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity(
            ClaimTypes.Permission, "message",
            ClaimTypes.Permission, "handler"));
        outcome = await middleware.InvokeAsync(context, new ProtectedCommand(), (_, _, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_Notification_ShouldRequireMessageAndHandlerPermissions()
    {
        var userContext = new UserContext();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity(
            ClaimTypes.Permission, "message",
            ClaimTypes.Permission, "handler"));
        var middleware = CreateMiddleware(userContext);
        var context = CreateNotificationContext(typeof(ProtectedNotification), typeof(NotificationHandlerWithPermission), nameof(NotificationHandlerWithPermission.Handle));

        var nextCalled = false;
        var outcome = await middleware.InvokeAsync(context, new ProtectedNotification(), (_, _, _) =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Empty.Instance);
        }, default);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_CustomAuthorizationHandler_ShouldReceiveFullContext()
    {
        var userContext = new UserContext();
        userContext.User.AddIdentity(CreateAuthenticatedIdentity());
        var handler = new TestInboundAuthorizationHandler();
        var middleware = CreateMiddleware(userContext, authorizationHandler: handler);
        var request = new TestRequest();

        var nextCalled = false;
        await middleware.InvokeAsync(
            CreateRequestContext(nameof(TestService.OverridePermissionMethod), handlerMethodName: nameof(TestHandler.ExecutePermissionHandler)),
            request,
            (_, forwardedRequest, _) =>
            {
                Assert.Same(request, forwardedRequest);
                nextCalled = true;
                return ValueTask.FromResult<object?>(Empty.Instance);
            },
            default);

        Assert.True(nextCalled);
        Assert.NotNull(handler.LastContext);
        Assert.Equal(typeof(TestService), handler.LastContext!.ServiceType);
        Assert.Same(request, handler.LastContext.Input);
        Assert.Equal(typeof(TestService), handler.LastContext.ServiceType);
        Assert.Equal(nameof(TestService.OverridePermissionMethod), handler.LastContext.ServiceMethodInfo?.Name);
        Assert.Equal(typeof(TestHandler), handler.LastContext.HandlerType);
        Assert.Equal(nameof(TestHandler.ExecutePermissionHandler), handler.LastContext.HandlerMethodInfo.Name);
        Assert.Equal(typeof(TestRequest), handler.LastContext.InputType);
    }

    private static Failure AsFailure(object? value) => value switch
    {
        AuthorizationInvocationFilter.AuthenticationRequired failure => new(failure.Title, failure.Detail),
        AuthorizationInvocationFilter.PermissionDenied failure => new(failure.Title, failure.Detail),
        AuthorizationInvocationFilter.PolicyDenied failure => new(failure.Title, failure.Detail),
        _ => throw new InvalidOperationException("Expected an authorization failure.")
    };

    private static InvocationContext CreateRequestContext(string serviceMethodName, Type? responseType = null, string? handlerMethodName = null, InvocationOrigin origin = InvocationOrigin.Http)
    {
        var serviceMethod = typeof(TestService).GetMethod(serviceMethodName)!;
        var handlerMethod = typeof(TestHandler).GetMethod(handlerMethodName ?? nameof(TestHandler.AllowAnonymousHandler))!;
        return new InvocationContext
        {
            HandlerType = typeof(TestHandler),
            HandlerMethodInfo = handlerMethod,
            Operation = new InvocationTestDescriptor(typeof(TestService), (serviceMethod).Name, typeof(TestRequest), responseType ?? typeof(Testing.TestOutcome)),
            Origin = origin
        };
    }

    private static InvocationContext CreateCommandContext(Type messageType, Type handlerType, string methodName)
    {
        return new InvocationContext
        {
            HandlerMethodInfo = handlerType.GetMethod(methodName)!,
            HandlerType = handlerType,
            Operation = new InvocationTestDescriptor(typeof(TestMessageService), nameof(TestMessageService.Handle), messageType, typeof(Empty)),
            Origin = InvocationOrigin.Message
        };
    }

    private static InvocationContext CreateNotificationContext(Type messageType, Type handlerType, string methodName)
    {
        return new InvocationContext
        {
            HandlerMethodInfo = handlerType.GetMethod(methodName)!,
            HandlerType = handlerType,
            Operation = new InvocationTestDescriptor(typeof(TestMessageService), nameof(TestMessageService.Handle), messageType, typeof(Empty)),
            Origin = InvocationOrigin.Message
        };
    }

    private static AuthorizationInvocationFilter CreateMiddleware(
        IUserContext userContext,
        IInboundAuthorizationHandler? authorizationHandler = null,
        TenantAuthorizationOptions? tenantOptions = null)
        => new(
            userContext,
            authorizationHandler ?? new DefaultInboundAuthorizationHandler(NullLogger<DefaultInboundAuthorizationHandler>.Instance),
            Options.Create(tenantOptions ?? new()));

    private static ClaimsIdentity CreateAuthenticatedIdentity(params string[] permissionClaims)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));

        for (var i = 0; i + 1 < permissionClaims.Length; i += 2)
        {
            identity.AddClaim(new Claim(permissionClaims[i], permissionClaims[i + 1]));
        }

        return identity;
    }

    [RequirePermission("message")]
    private sealed class TestMessageService { public void Handle(object input) { } }

    [RequirePermission("input-type")]
    private sealed class TestService
    {
        [UseHostTenant]
        [RequirePermission("platform.manage")]
        public void HostMethod(TestRequest request) { }

        [AllowAnonymous]
        public void AllowAnonymousMethod(TestRequest request) { }

        [RequirePermission]
        public void LoginOnlyMethod(TestRequest request) { }

        [RequirePermission("input-method")]
        public void OverridePermissionMethod(TestRequest request) { }
    }

    private sealed class TestHandler
    {
        [AllowAnonymous]
        public void AllowAnonymousHandler(TestRequest request) { }

        [RequirePermission("execute-method")]
        public void ExecutePermissionHandler(TestRequest request) { }
    }

    private sealed class TestRequest;

    [RequirePermission("message")]
    private sealed class ProtectedCommand;

    [RequirePermission("message")]
    private sealed class ProtectedNotification;

    private sealed class CommandHandlerWithPermission
    {
        [RequirePermission("handler")]
        public void Handle(ProtectedCommand command) { }
    }

    private sealed class NotificationHandlerWithPermission
    {
        [RequirePermission("handler")]
        public void Handle(ProtectedNotification notification) { }
    }

    private sealed class TestInboundAuthorizationHandler : IInboundAuthorizationHandler
    {
        public InboundAuthorizationContext? LastContext { get; private set; }

        public ValueTask<InboundAuthorizationResult> AuthorizeAsync(
            InboundAuthorizationContext context,
            CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastContext = context;
            return ValueTask.FromResult<InboundAuthorizationResult>(InboundAuthorizationResult.Success);
        }
    }
}
