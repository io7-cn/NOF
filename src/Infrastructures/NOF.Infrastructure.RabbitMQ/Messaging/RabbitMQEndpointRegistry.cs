using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>Maps one existing RabbitMQ queue to an application operation.</summary>
public sealed record RabbitMQEndpoint(string QueueName, string ConsumerId, ServiceOperationDescriptor Operation);

/// <summary>Owns RabbitMQ queue consumers only; exchange topology belongs to the application.</summary>
public sealed class RabbitMQEndpointRegistry : Registry<RabbitMQEndpoint>
{
    /// <summary>Gets validated queue endpoints, frozen on first use.</summary>
    public IReadOnlyList<RabbitMQEndpoint> Endpoints
    {
        get
        {
            var endpoints = Freeze().ToArray();
            if (endpoints.GroupBy(endpoint => endpoint.QueueName, StringComparer.Ordinal).Any(group => group.Count() != 1)
                || endpoints.GroupBy(endpoint => endpoint.ConsumerId, StringComparer.Ordinal).Any(group => group.Count() != 1)
                || endpoints.Any(endpoint => endpoint.Operation.ResponseType != typeof(Empty) || !ServiceOperationInspector.CanSubmit(endpoint.Operation)))
            {
                throw new InvalidOperationException("Each RabbitMQ queue must select exactly one Empty operation without declared failures.");
            }
            return endpoints;
        }
    }
}
