using System.Text;

namespace NOF.Infrastructure;

public interface IObjectSerializer
{
    ReadOnlyMemory<byte> Serialize(object? value, Type? runtimeType = null);

    object? Deserialize(ReadOnlyMemory<byte> data, Type runtimeType);
}
