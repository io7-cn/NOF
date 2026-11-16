# NOF.Infrastructure.AmazonS3

[English](../../en/packages/amazon-s3.md)

AWS S3 与兼容服务的对象存储 Provider，业务继续依赖 IObjectStorage，基础设施替换 IObjectStorageRider。

## AWS 与兼容端点

```csharp
builder.Services.AddObjectStorage().AddAmazonS3ObjectStorage(options =>
{
    options.Region = "ap-southeast-1";
});
```

未明确提供凭据时，使用 AWS SDK 的默认凭据链。MinIO、Ceph、Cloudflare R2 等兼容端点可以配置 ServiceUrl、Region、ForcePathStyle、AccessKeyId、SecretAccessKey 与 UseChunkEncoding；只配置 ServiceUrl 时，签名 Region 默认 us-east-1。凭据应从宿主配置读取，不写入代码。需要高级设置时，可提供原生客户端工厂。

```csharp
builder.Services.AddObjectStorage().AddAmazonS3ObjectStorage(options =>
{
    options.ServiceUrl = "https://s3.example.com";
    options.Region = "us-east-1";
    options.ForcePathStyle = true;
    options.AccessKeyId = configuration["S3:AccessKeyId"];
    options.SecretAccessKey = configuration["S3:SecretAccessKey"];
    options.UseChunkEncoding = false;
});
```

## 浏览器直接上传和下载

此 Provider 的 SupportsPresignedRequests 为 true。服务端先完成业务授权并选择 Bucket 与逻辑 Key，再签发短期请求：

```csharp
var upload = await objectStorage.CreatePresignedUploadAsync(
    "documents", $"uploads/{Guid.NewGuid():N}.pdf", TimeSpan.FromMinutes(10),
    new ObjectStorageWriteOptions { ContentType = "application/pdf" }, cancellationToken);
var download = await objectStorage.CreatePresignedDownloadAsync(
    "documents", "reports/42.pdf", TimeSpan.FromMinutes(5), cancellationToken);
```

返回的 Url、Method、Headers 与 ExpiresAt 可传给浏览器。它们与普通 Put/OpenRead 应用相同的租户 KeyPrefix；IgnoreKeyPrefix 是明确的管理级绕过。签名 URL 自带临时授权，不要改写 Host/Path 或记录到日志；签名前应保证端点可被浏览器访问。

```javascript
const response = await fetch(upload.url, {
  method: upload.method,
  headers: upload.headers,
  body: file,
});
if (!response.ok) {
  throw new Error(`Upload failed: ${response.status}`);
}
```

请求头必须保持不变，上传内容头与自定义元数据也参与签名。Bucket 的 CORS（跨来源资源共享）需要允许前端 Origin、PUT/GET 与所需请求头，签名不会绕过浏览器 CORS。预签名 PUT 可覆盖相同 Key，并可在到期前重复使用。签名不上传数据、不确认下载对象存在，也不提供分片上传、POST 策略、大小限制或上传后校验。

有效期为 1 秒至 7 天，向下取整到 UTC 秒；临时凭据或存储策略可能让请求提前失效。底层 SDK 签名入口不接受 CancellationToken，NOF 可以取消等待，但底层凭据解析可能继续。参见 [AWS 官方指南](https://docs.aws.amazon.com/AmazonS3/latest/userguide/using-presigned-url.html)与[对象存储边界](../design/object-storage.md)。

## 安装

```sh
dotnet add package NOF.Infrastructure.AmazonS3
```

Apache-2.0。
