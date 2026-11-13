# 聚合仓储与持久化边界

[English](../../en/design/persistence-boundaries.md) · [开发指南](../README.md)

Domain/Application 不再公开通用 ORM 外观。领域仓储加载完整聚合，应用读取接口返回 DTO；
用例通过 IUnitOfWork.SaveChangesAsync(CancellationToken) 统一保存，返回 Task，
不依赖各 Provider 含义不同的变更数量。核心层不再提供 Set<T>、IQueryable/ICollection 仓储、
Attach/Update、跟踪开关、查询执行器或更新表达式 DSL。

```csharp
public interface IConfigNodeRepository
{
    Task<ConfigNode?> GetAsync(ConfigNodeId id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(ConfigNodeName name, CancellationToken ct = default);
    void Add(ConfigNode node);
    void Remove(ConfigNode node);
}

var node = await nodes.GetAsync(id, cancellationToken);
if (node is null)
{
    return new NotFound("Node not found.");
}
node.UpdateParent(parentId);
await outbox.EnqueueAsync(message, context, cancellationToken: cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
```

GetAsync 加载可修改的完整聚合及其子实体；Add/Remove 登记变更，仓储不自行保存。
子实体通过聚合方法修改。读取接口负责完成查询并返回业务数据，不把 Provider 查询对象交给调用方。
实现内部可以使用原生 LINQ、SQL 和映射表达式。唯一性约束和乐观并发仍由具体持久化实现保证，
预检查不能消除竞争。不新增通用 Specification 语言或强制聚合标记接口。

## 配置节点落地

Domain 定义 IConfigNodeRepository；Application 定义 IConfigNodeQueries 和 IConfigNodeHierarchyStore。
后者在领域事件处理中维护层级投影，随聚合一起保存，不独立提交。
全部 CRUD、文件、层级和配置查询 Handler 均依赖这些接口。
宿主的 ConfigNodePersistence 实现三个接口，共享授权后创建的 scoped Provider Session。
DTO 读取不返回实体，层级集合返回副本。既有映射、校验、缓存键和 HTTP 业务结果保持一致。
加入其 ApplicationPart 后由 AutoInject 注册；集成测试链接同一适配器，验证内存与关系数据库路径。

## 事务和可靠消息

ITransactionManager.BeginTransactionAsync 返回最小 ITransaction，只提供异步 CommitAsync、
RollbackAsync 和释放。无显式事务时使用 Provider 的原子保存；有显式事务时，SaveChangesAsync
只刷新当前事务，CommitAsync 才提交。多次保存不会自动提交外部事务。
Savepoint、原生事务标识由具体 Provider 处理，不再属于公共事务契约。

scoped PersistenceUnitOfWork 与聚合适配器、投影存储、Outbox 暂存及 Inbox 处理共用同一 Session。
需要原子性的操作不得另建上下文。Outbox 暂存不立即发送；失败、回滚或提交结果不确定后，
丢弃作用域，以新上下文和业务幂等键恢复。提交异常不代表数据库一定回滚。

基础设施的 IInboxStore/IOutboxStore 提供接收、幂等检查、认领、顺序状态、恢复、完成、失败、
保留期清理和暂存操作。工作器、内存及 RabbitMQ Rider 使用这些接口。
默认存储保留原子状态迁移；业务 Handler 写入和 Inbox 完成仍共享事务。
重试、租约过期、顺序和取消继续由现有消息回归验证。

## Provider 实现边界

Infrastructure 保留 IPersistenceSession、IEntitySet、IProviderQuery/IProviderQueryExecutor 和
Provider 更新表达式，仅作为共享存储实现之间的底层协作，不是业务层的替代仓储接口。
Session 仅保留 Set<T>、异步保存和异步开启事务。
EntitySet 不再继承 ICollection，也不提供 Attach/Update/Range/Clear/CopyTo。
核心程序集不引用 Infrastructure。宿主适配器仍可直接使用 EF/NHibernate 的原生机制。
异步查询便利方法需显式引入 Infrastructure 命名空间，不再作为全局 System.Linq 持久化扩展；对应回归迁至 Infrastructure，纯表达式映射继续保留在 Application。

本轮收窄的是业务依赖边界，不承诺各 ORM 具有相同执行语义。
原生批量 SQL 可能立即执行、绕过跟踪器，与其他写入组合时需显式事务；这些操作留在存储实现内部。

## 破坏性迁移

删除 Domain 的 IRepository/IAsyncQueryable/IAsyncQueryExecutor/更新表达式及 Application 的
IDbContext/IDbContextFactory/IDbContextTransaction/异步查询便利方法，不保留核心旧 API 的转发别名。
定义领域仓储和应用 DTO 读取接口，注入 IUnitOfWork 保存，需要显式事务时注入 ITransactionManager。
原生查询及持久化组合移至宿主/Provider。基础设施实现通过底层协作接口和 AddPersistenceServices
注册，业务层不要将其重新当作通用 ORM 使用。自定义 Provider 工厂和 Mock 一同迁移。
Range、跟踪和 Savepoint 操作使用具体原生 API。
所有包、接口、适配器和测试协调升级；回滚恢复配套旧版本。本次 API 迁移不要求变更数据库结构。
