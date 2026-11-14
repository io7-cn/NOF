using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NOF.Application;
using NOF.Domain;
using NOF.Infrastructure;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics.CodeAnalysis;
namespace NOF.Hosting;

public static partial class NOFInfrastructureExtensions
{
    private static readonly object InfrastructureConfiguredKey = new();
    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddApplicationService<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApplicationService>()
            where TApplicationService : ApplicationService, IApplicationServiceImplementation
        {
            builder.Services.AddApplicationService<TApplicationService>();
            return builder;
        }

        /// <summary>Registers default server infrastructure on a native host builder without building or starting it.</summary>
        public IHostApplicationBuilder AddNOF()
        {
            if (builder.Properties.ContainsKey(InfrastructureConfiguredKey))
            {
                return builder;
            }
            builder.Properties[InfrastructureConfiguredKey] = true;
            builder.AddNOFHosting();
            builder.Services.AddNOFApplication();
            builder.Services.AddInMemoryMessagingPersistence();
            AddOpenTelemetry(builder);

            #region Core Services
            builder.Services.TryAddSingleton<ICacheLockRetryStrategy, ExponentialBackoffCacheLockRetryStrategy>();
            builder.Services.TryAddSingleton<IObjectSerializer, JsonObjectSerializer>();
            builder.Services.AddHttpClient<HttpAuthorizationServerService>();
            builder.Services.Replace(ServiceDescriptor.Singleton<IIdGenerator>(sp => new SnowflakeIdGenerator(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SnowflakeIdGeneratorOptions>>().Value,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DeploymentOptions>>().Value.ServiceId,
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DeploymentOptions>>().Value.InstanceId)));
            builder.Services.TryAddScoped<IInboundAuthorizationHandler, DefaultInboundAuthorizationHandler>();
            builder.Services.TryAddScoped<IOperationInvoker, OperationInvoker>();

            builder.Services.TryAddSingleton<MemoryCacheServiceRiderState>();
            builder.Services.TryAddSingleton<CacheServiceLocalLockState>();
            builder.Services.TryAddSingleton<MemoryBackplaneState>();
            builder.Services.TryAddSingleton<ITenantProvider, HostTenantProvider>();
            builder.Services.TryAddScoped<ICacheService>(sp => new CacheService(
                sp.GetRequiredService<ICacheServiceRider>(),
                sp.GetRequiredService<IObjectSerializer>(),
                sp.GetRequiredService<ICacheLockRetryStrategy>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CacheServiceOptions>>(),
                sp.GetRequiredService<CacheServiceLocalLockState>()));
            builder.Services.TryAddScoped<IDistributedCache>(sp => sp.GetRequiredService<ICacheService>());
            builder.Services.TryAddScoped<IObjectStorage>(sp => new ObjectStorageService(
                sp.GetRequiredService<IObjectStorageRider>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ObjectStorageOptions>>()));
            builder.Services.TryAddSingleton<IBackplane>(sp => new MemoryBackplane(
                sp.GetRequiredService<MemoryBackplaneState>()));

            #endregion

            #region Options
            builder.Services.AddOptions<CacheServiceOptions>();
            builder.Services.AddOptions<ObjectStorageOptions>();
            builder.Services.AddOptions<FileSystemObjectStorageOptions>();
            builder.Services.AddOptions<AuthenticationResourceServerOptions>();
            builder.Services.AddOptions<SnowflakeIdGeneratorOptions>()
                .Validate(static options => options.ApplicationIdBits > 0, "ApplicationIdBits must be greater than zero.")
                .Validate(static options => options.InstanceIdBits > 0, "InstanceIdBits must be greater than zero.")
                .Validate(static options => options.SequenceBits > 0, "SequenceBits must be greater than zero.")
                .Validate(
                    static options => options.ApplicationIdBits + options.InstanceIdBits + options.SequenceBits <= 22,
                    "The sum of ApplicationIdBits, InstanceIdBits, and SequenceBits must be less than or equal to 22.");
            #endregion

            #region Pipelines
            builder.Services.TryAddScoped<IOperationExecutor, OperationExecutor>();
            builder.Services.TryAddSingleton<MessageInboxReceiver>();
            builder.Services.GetOrAddSingleton<MemoryEndpointRegistry>();
            #endregion


            // Host defaults select the in-memory engine; a host can replace IInvocationPipeline.
            builder.Services.UseInMemoryInvocationPipeline();
            builder.Services.TryAddScoped<MessageDelivery>();
            builder.Services.AddInvocationFilter<TracingInvocationFilter>(-400);
            builder.Services.AddInvocationFilter<InstrumentationInvocationFilter>(-300);
            builder.Services.AddInvocationFilter<TenantInvocationFilter>(-100);
            builder.Services.AddInvocationFilter<AuthorizationInvocationFilter>(-50);
            builder.Services.AddInvocationFilter<HostTenantInvocationFilter>(-25);

            #region Default Persistence
            builder.Services.TryAddScoped<ICacheServiceRider>(sp => new MemoryCacheServiceRider(
                sp.GetRequiredService<MemoryCacheServiceRiderState>()));
            builder.Services.TryAddScoped<IObjectStorageRider, FileSystemObjectStorageRider>();
            builder.Services.TryAddScoped<MemoryMessageClient>();
            #endregion

            return builder;
        }


    }

    private static void AddOpenTelemetry(IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(NOFInfrastructureConstants.InboundPipeline.MeterName);
                metrics.AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(NOFInfrastructureConstants.InboundPipeline.ActivitySourceName);
                tracing.AddSource(NOFInfrastructureConstants.OutboundPipeline.ActivitySourceName);
                tracing.AddHttpClientInstrumentation();
            });

        const string otelExporterOtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration[otelExporterOtlpEndpoint]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
    }
}
