using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Application.EventHandlers;

/// <summary>
/// 节点删除时从父节点的子节点列表中移除
/// </summary>
public class RemoveChildNodeOnConfigNodeDeleted : LocalEventHandler<ConfigNodeDeletedEvent>
{
    private readonly IConfigNodeHierarchyStore _hierarchy;

    public RemoveChildNodeOnConfigNodeDeleted(IConfigNodeHierarchyStore hierarchy)
    {
        _hierarchy = hierarchy;
    }

    public override async Task HandleAsync(ConfigNodeDeletedEvent @event, Context context, CancellationToken cancellationToken)
    {
        if (@event.ParentId.HasValue)
        {
            var parentChildren = await _hierarchy.GetAsync(@event.ParentId.Value, cancellationToken);
            parentChildren?.RemoveChild(@event.Id);
        }

        var nodeChildren = await _hierarchy.GetAsync(@event.Id, cancellationToken);

        if (nodeChildren is not null)
        {
            _hierarchy.Remove(nodeChildren);
        }
    }
}
