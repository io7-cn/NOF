using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Application.EventHandlers;

/// <summary>
/// 节点创建时添加到子节点列表
/// </summary>
public class AddChildNodeOnConfigNodeCreated : LocalEventHandler<ConfigNodeCreatedEvent>
{
    private readonly IConfigNodeHierarchyStore _hierarchy;

    public AddChildNodeOnConfigNodeCreated(IConfigNodeHierarchyStore hierarchy)
    {
        _hierarchy = hierarchy;
    }

    public override async Task HandleAsync(ConfigNodeCreatedEvent @event, Context context, CancellationToken cancellationToken)
    {
        if (@event.ParentId.HasValue)
        {
            var parentChildren = await _hierarchy.GetAsync(@event.ParentId.Value, cancellationToken);

            if (parentChildren is null)
            {
                parentChildren = Entities.ConfigNodeChildren.Create(@event.ParentId.Value);
                _hierarchy.Add(parentChildren);
            }

            parentChildren.AddChild(@event.Id);
        }

        _hierarchy.Add(Entities.ConfigNodeChildren.Create(@event.Id));
    }
}
