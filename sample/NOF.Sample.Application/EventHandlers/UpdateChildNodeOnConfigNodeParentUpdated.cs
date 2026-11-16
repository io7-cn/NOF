using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Application.EventHandlers;

/// <summary>
/// 节点父节点更新时同步更新子节点列表
/// </summary>
public class UpdateChildNodeOnConfigNodeParentUpdated : LocalEventHandler<ConfigNodeParentUpdatedEvent>
{
    private readonly IConfigNodeHierarchyStore _hierarchy;

    public UpdateChildNodeOnConfigNodeParentUpdated(IConfigNodeHierarchyStore hierarchy)
    {
        _hierarchy = hierarchy;
    }

    public override async Task HandleAsync(ConfigNodeParentUpdatedEvent @event, Context context, CancellationToken cancellationToken)
    {
        if (@event.OldParentId.HasValue)
        {
            var oldParentChildren = await _hierarchy.GetAsync(@event.OldParentId.Value, cancellationToken);
            oldParentChildren?.RemoveChild(@event.NodeId);
        }

        if (@event.NewParentId.HasValue)
        {
            var newParentChildren = await _hierarchy.GetAsync(@event.NewParentId.Value, cancellationToken);

            if (newParentChildren is null)
            {
                newParentChildren = Entities.ConfigNodeChildren.Create(@event.NewParentId.Value);
                _hierarchy.Add(newParentChildren);
            }

            newParentChildren.AddChild(@event.NodeId);
        }
    }
}
