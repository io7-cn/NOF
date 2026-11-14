using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NOF.Application;
using NOF.Infrastructure;
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
            builder.Services.TryAddScoped<IInboundAuthorizationHandler, DefaultInboundAuthorizationHandler>();
            builder.Services.TryAddScoped<IOperationInvoker, OperationInvoker>();
            builder.Services.TryAddScoped<IOperationExecutor, OperationExecutor>();
            builder.Services.UseInMemoryInvocationPipeline();
            builder.Services.AddInvocationFilter<TracingInvocationFilter>(-400);
            builder.Services.AddInvocationFilter<InstrumentationInvocationFilter>(-300);
            builder.Services.AddInvocationFilter<TenantInvocationFilter>(-100);
            builder.Services.AddInvocationFilter<AuthorizationInvocationFilter>(-50);

            return builder;
        }


    }

}
