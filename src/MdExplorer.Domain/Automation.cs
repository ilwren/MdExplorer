namespace MdExplorer.Domain;

/// <summary>自动化节点类型。v0.21 第一版只支持一条可解释的线性流水线，不做任意 DAG 或脚本求值。</summary>
public enum AutomationNodeKind
{
    CurrentFolder,
    KindFilter,
    NameContains,
    TransferToSubfolder,
    SystemNotification,
}

public enum AutomationTransferMode
{
    Copy,
    Move,
}

/// <summary>
/// 一个可持久化自动化节点。参数按节点类型显式分开保存，避免用无类型字典在 JSON 中
/// 漂移；坐标只服务节点画布，执行顺序只由连线决定。
/// </summary>
public sealed class AutomationNodeDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AutomationNodeKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>类型筛选目标；null 表示任意文件类型。Folder 在执行校验中不合法，因为来源只取顶层文件。</summary>
    public FileKind? FilterKind { get; set; }

    /// <summary>名称包含筛选（不区分大小写）；空字符串表示不按名称收窄。</summary>
    public string NameQuery { get; set; } = string.Empty;

    /// <summary>目标子文件夹名，只能是当前文件夹下的一级名称，不能包含路径分隔符。</summary>
    public string DestinationSubfolder { get; set; } = "已整理";

    public AutomationTransferMode TransferMode { get; set; } = AutomationTransferMode.Copy;

    public string NotificationTitle { get; set; } = "自动化完成";

    /// <summary>支持 {count}、{succeeded}、{failed}、{destination} 四个有界占位符。</summary>
    public string NotificationBody { get; set; } = "已处理 {succeeded}/{count} 个文件到 {destination}。";
}

public sealed class AutomationConnectionDefinition
{
    public string SourceNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
}

/// <summary>单个自动化工作流的完整定义。当前产品只持久化一条工作流，但模型不把节点数量写死。</summary>
public sealed class AutomationWorkflowDefinition
{
    public string Name { get; set; } = "整理当前文件夹";
    public List<AutomationNodeDefinition> Nodes { get; set; } = [];
    public List<AutomationConnectionDefinition> Connections { get; set; } = [];

    public static AutomationWorkflowDefinition CreateDefault() => new()
    {
        Name = "整理当前文件夹",
        Nodes =
        [
            new AutomationNodeDefinition
            {
                Id = "source",
                Kind = AutomationNodeKind.CurrentFolder,
                X = 24,
                Y = 128,
            },
            new AutomationNodeDefinition
            {
                Id = "kind-filter",
                Kind = AutomationNodeKind.KindFilter,
                X = 250,
                Y = 128,
                FilterKind = FileKind.Image,
            },
            new AutomationNodeDefinition
            {
                Id = "transfer",
                Kind = AutomationNodeKind.TransferToSubfolder,
                X = 476,
                Y = 128,
                DestinationSubfolder = "图片整理",
                TransferMode = AutomationTransferMode.Copy,
            },
            new AutomationNodeDefinition
            {
                Id = "notify",
                Kind = AutomationNodeKind.SystemNotification,
                X = 702,
                Y = 128,
                NotificationTitle = "自动化完成",
                NotificationBody = "已处理 {succeeded}/{count} 个文件到 {destination}。",
            },
        ],
        Connections =
        [
            new AutomationConnectionDefinition { SourceNodeId = "source", TargetNodeId = "kind-filter" },
            new AutomationConnectionDefinition { SourceNodeId = "kind-filter", TargetNodeId = "transfer" },
            new AutomationConnectionDefinition { SourceNodeId = "transfer", TargetNodeId = "notify" },
        ],
    };
}
