# Object storage boundaries

[简体中文](../../zh-CN/design/object-storage.md)

Application uses IObjectStorage and streaming content; the host selects a rider with AddObjectStorage. Optional<T> models missing objects. Tenant isolation uses ObjectStorageOptions.KeyPrefix, including `{tenantId}`; IgnoreKeyPrefix is an explicit administrative view and needs application authorization.

## Local and memory storage

```csharp
services.AddObjectStorage().AddFileSystemObjectStorage(options => options.RootPath = "/data/objects");
// For isolated tests:
services.AddObjectStorage().AddMemoryObjectStorage();
```

The file-system default root is AppContext.BaseDirectory/App_Data/objects. Relative configured paths resolve against the working directory. Persist a writable directory for durable use. The rider owns its hashed files containing metadata and content: do not modify them or add symbolic links. Writes atomically replace whole objects; reads retain a snapshot across replacement/deletion. Listing scans metadata, uses ordinal key order and prefix matching. Memory state is shared across scopes in one host and ends with that host; AddNOFTest selects it explicitly.

PutAsync owns no caller input stream; callers release streams they create. Dispose the content stream returned by OpenReadAsync. IObjectStorage is a storage port, not an HTTP server or transaction coordinator.

## Direct HTTP transfers

SupportsPresignedRequests reports signing support. CreatePresignedUploadAsync/CreatePresignedDownloadAsync return URL, method, required headers and expiration, with the same tenant prefix policy. File-system and memory riders report false and throw NotSupportedException because they expose no HTTP endpoint. Default interface behavior keeps custom riders without signing support explicit.

Authorize signing in the application before issuing the URL. Treat it as a temporary credential, not an object name to log or rewrite. S3 supports single-request PUT/GET signing; browser CORS, overwrite/reuse, content validation and credential expiration remain deployment/application concerns. See the complete [S3 provider guide](../packages/amazon-s3.md).
