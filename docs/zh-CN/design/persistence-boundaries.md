# 聚合仓储与原生持久化

[English](../../en/design/persistence-boundaries.md) · [开发指南](../README.md)

Domain 声明聚合专用仓储接口，Application 声明 DTO 查询端口和 IUnitOfWork。接口只表达业务操作与数据，
不暴露 IQueryable、跟踪开关、实体集合或查询语言。宿主通过选定 Provider 的原生 API 实现这些端口。

```csharp
public interface IConfigNodeRepository
{
    Task<ConfigNode?> GetAsync(ConfigNodeId id, CancellationToken ct = default);
    void Add(ConfigNode node);
    void Remove(ConfigNode node);
}

// 宿主中的原生 EF 实现，与 NOF Store 共用作用域内的 DbContext。
public sealed class ConfigNodePersistence(DbContext context) : IConfigNodeRepository
{
    public Task<ConfigNode?> GetAsync(ConfigNodeId id, CancellationToken ct = default)
        => context.Set<ConfigNode>().FirstOrDefaultAsync(node => node.Id == id, ct);
    public void Add(ConfigNode node) => context.Set<ConfigNode>().Add(node);
    public void Remove(ConfigNode node) => context.Set<ConfigNode>().Remove(node);
}
```

可编译示例中的适配器还实现了配置读取与层级投影端口。原生 EF 查询继续支持映射表达式、Owned 图、唯一约束、
租户过滤与软删除约定。仓储只暂存，由 Handler 通过共享 IUnitOfWork 保存；存在性预检查不能替代数据库约束。

## 共享工作单元

IUnitOfWork.SaveChangesAsync 返回 Task，不传播 Provider 的变更计数。可选 ITransactionManager.BeginTransactionAsync
返回提供异步提交、回滚与释放的 ITransaction。显式事务内保存只负责 Flush，Commit 才使变更持久化。
未完成事务需要释放或回滚，重试前丢弃当前作用域；提交结果不确定时应进行幂等恢复，不能假定已经回滚。

原生持久化保留 IOutboxStore 与业务共享的 IUnitOfWork/事务集成，不再提供 Inbox Store、收件、认领或消费模型。消息端点不要求数据库，业务 Handler/Filter 决定保存、事务和原子幂等约束。EF/NHibernate Outbox 保留有界认领、租户隔离、延迟、租约恢复和所有权校验。部署前解决旧 Inbox 工作并审查防护删表迁移，见 [Outbox 与迁移](transactional-outbox.md)。

## 原生 Provider 实现

完整移除跨 ORM 协作层：IPersistenceSession/Factory/Transaction、IEntitySet、IProviderQuery/Executor、ProviderQuery、
共享异步查询扩展、UpdateSetters、IDbModelBuilder 和 IDbContextModelCreatingContributor 均不再提供。
Provider 不再翻译 NOF 查询、更新或建模语言；通用内存 ORM 与反射跟踪器也一并删除，不保留兼容别名或实现。

EF 模型扩展在 EF Provider 包中实现 IEFCoreModelConfiguration.Configure(ModelBuilder)，直接使用原生 EF API。
NHibernate 扩展注册 Action<ModelMapper> 回调，直接使用原生 Mapping By Code。值对象转换，以及 EF 的长度、集合、
租户和软删除约定继续由 Provider 提供。Application 不注入模型配置或原生会话。

## 身份持久化

OIDC 协议与存储集成包已移除，宿主直接使用上游方案。Sample 将 OpenIddict 模型并入现有 ConfigurationDbContext 与迁移链，
为其关闭业务软删除约定。新迁移保留旧身份表，凭据与会话过渡须显式审查。参见[宿主装配与身份迁移](host-composition.md)。

## 内存消息与测试

消息能力在未选择原生 Provider 时使用开发用内存后端。AddInMemoryMessagingPersistence 注册宿主内、按租户分区的状态和作用域消息工作单元；
AddOutbox 在未选择原生 Provider 或自定义 Store 时使用它。消息能力及 Worker 仍按需开启，
原生 Provider、自定义 Store 与能力装配的前后顺序均受支持。

此后端只保存发件意图，可原子提交暂存消息，并提供租户隔离与领取者校验；不再提供任意业务实体表或
ORM API。业务持久化测试应选择真实 Provider，例如 UseDbContext 配合内存 SQLite，或者注册业务专用测试端口与
适当的工作单元。NOF.Test 不会自动将任意领域对象变为持久化实体。

## 迁移

基础设施适配器将被移除的协作接口依赖改为原生 DbContext/ISession，业务端口保持不变；注册原生模型配置与可选
原生身份集成。AddInMemoryPersistence/AddPersistenceServices 改为明确选择原生 Provider；消息专用内存默认实现无需 ORM 包。

原生查询与建模 API 改造本身不重命名表或重新解释数据。部署前审查 Provider 迁移，并明确升级每个租户数据库。

## 持久化异常

Provider 将原生乐观并发保存失败转换为 System.Data.DBConcurrencyException，将数据保存失败转换为 System.Data.DataException，并保留原始原因。Application 不定义数据库异常体系。事务失败、取消及意外异常原样传播；消费端业务自行决定事务与幂等策略，不经过通用收件去重。
