using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Contract;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOF.Infrastructure;
/// <summary>Provides generated JSON metadata for local object storage records.</summary>

[JsonSerializable(typeof(ObjectStorageObjectInfo))]
public partial class FileSystemObjectStorageJsonContext : JsonSerializerContext;
