namespace NOF.Contract;

/// <summary>
/// Marker interface for source-generated application service clients.
/// </summary>
public interface IApplicationServiceClient;

/// <summary>
/// Associates an application service client contract with the application service contract it invokes.
/// </summary>
/// <typeparam name="TService">The application service contract.</typeparam>
public interface IApplicationServiceClient<TService> : IApplicationServiceClient
    where TService : IApplicationService;
