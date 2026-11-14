using NOF.Contract;
using System.ComponentModel;

namespace NOF.Sample;

public interface INOFSampleService : IApplicationService
{
    [Summary("创建配置节点")]
    [Description("在指定父节点下创建一个新的配置节点，若不指定父节点则创建为根节点")]
    [Category("配置节点")]
    [FailureCase("NameConflict", "config_node.name_conflict")]
    [FailureCase("ParentNotFound", "config_node.parent_not_found")]
    [FailureCase("InvalidInput", "config_node.invalid_input")]
    CreatedConfigNode CreateConfigNode(CreateConfigNodeRequest request);

    [Summary("删除配置节点")]
    [Description("根据节点 ID 删除指定的配置节点")]
    [Category("配置节点")]
    [FailureCase("NotFound", "config_node.not_found")]
    [FailureCase("HasChildren", "HasChildren", typeof(NodeHasChildrenError))]
    Empty DeleteConfigNode(DeleteConfigNodeRequest request);

    [Summary("新增或更新配置文件")]
    [Description("在指定节点下新增或更新一个配置文件的内容")]
    [Category("配置文件")]
    [FailureCase("NotFound", "config_node.not_found")]
    [FailureCase("InvalidInput", "config_node.invalid_input")]
    Empty AddOrUpdateConfigFile(AddOrUpdateConfigFileRequest request);

    [Summary("删除配置文件")]
    [Description("从指定节点中移除一个配置文件")]
    [Category("配置文件")]
    [FailureCase("NotFound", "config_node.not_found")]
    [FailureCase("InvalidInput", "config_node.invalid_input")]
    Empty RemoveConfigFile(RemoveConfigFileRequest request);

    [Summary("设置活跃配置文件")]
    [Description("设置指定节点的当前活跃配置文件，传入 null 可取消活跃文件")]
    [Category("配置文件")]
    [FailureCase("NotFound", "config_node.not_found")]
    [FailureCase("InvalidInput", "config_node.invalid_input")]
    Empty SetActiveFile(SetActiveFileRequest request);

    [Summary("更新节点父级")]
    [Description("将指定配置节点移动到新的父节点下，传入 null 可将其设为根节点")]
    [Category("配置节点")]
    [FailureCase("NotFound", "config_node.not_found")]
    [FailureCase("ParentCycle", "config_node.parent_cycle")]
    [FailureCase("ParentNotFound", "config_node.parent_not_found")]
    Empty UpdateConfigNodeParent(UpdateConfigNodeParentRequest request);

    [Summary("查询指定节点的子节点列表")]
    [Description("根据父节点 ID 获取其直接子节点的 ID 列表")]
    [Category("配置节点")]
    [FailureCase("NotFound", "config_node.not_found")]
    GetConfigNodeChildrenResponse GetConfigNodeChildren(GetConfigNodeChildrenRequest request);

    [Summary("查询单个配置节点")]
    [Description("根据节点 ID 获取配置节点的详细信息")]
    [Category("配置节点")]
    [FailureCase("NotFound", "config_node.not_found")]
    GetConfigNodeByIdResponse GetConfigNodeById(GetConfigNodeByIdRequest request);

    [Summary("查询所有根配置节点")]
    [Description("获取所有没有父节点的顶层配置节点列表")]
    [Category("配置节点")]
    GetRootConfigNodesResponse GetRootConfigNodes(GetRootConfigNodesRequest request);

    [FailureCase("NotFound", "config_node.not_found")]
    [FailureCase("InvalidInput", "config_node.invalid_input")]
    GetConfigurationResponse GetConfiguration(GetConfigurationRequest request);

    [Summary("流式传输演示")]
    [Description("返回演示事件流")]
    [Category("流式传输")]
    [FailureCase("StreamUnavailable", "sample_stream_failure")]
    IAsyncEnumerable<SampleStreamEvent> WatchSampleStream(WatchSampleStreamRequest request);

}
