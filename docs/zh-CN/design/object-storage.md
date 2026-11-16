# 对象存储边界

[English](../../en/design/object-storage.md)

Application 通过 IObjectStorage 操作流式内容，由宿主用 AddObjectStorage 选择 Rider；Optional<T> 表示对象缺失。租户隔离使用 ObjectStorageOptions.KeyPrefix，包括 `{tenantId}`；IgnoreKeyPrefix 是明确的管理视图，需要业务授权。

## 本地与内存存储

```csharp
services.AddObjectStorage().AddFileSystemObjectStorage(options => options.RootPath = "/data/objects");
// 隔离测试使用：
services.AddObjectStorage().AddMemoryObjectStorage();
```

文件系统默认根目录为 AppContext.BaseDirectory/App_Data/objects；配置的相对路径以工作目录为基准。需要持久化时提供可写且持久的目录。Rider 独占管理包含元数据和内容的散列文件，不要直接修改或添加符号链接。写入原子替换完整对象，读取在替换或删除后仍保留原快照。列表扫描元数据，按 ordinal 键顺序和前缀匹配。内存状态在同一宿主的作用域之间共享，随宿主释放；AddNOFTest 会明确选择它。

PutAsync 不持有调用方输入流，创建流的一方负责释放；OpenReadAsync 返回的内容流也需要释放。IObjectStorage 是存储端口，不提供 HTTP 服务或事务协调。

## 直接 HTTP 传输

SupportsPresignedRequests 表示签名能力；CreatePresignedUploadAsync/CreatePresignedDownloadAsync 返回 URL、方法、所需请求头和到期时间，使用相同租户前缀。文件系统和内存 Rider 因没有 HTTP 端点而返回 false，签名时抛出 NotSupportedException；默认接口实现也明确保留自定义 Rider 不支持签名的状态。

签发前由应用授权，URL 应作为临时凭据处理，不记录或改写。S3 提供单请求 PUT/GET 签名；浏览器跨来源资源共享（CORS）、覆盖/重复使用、内容校验与凭据到期仍属于部署或应用职责。参见完整的 [S3 Provider 指南](../packages/amazon-s3.md)。
