using MdExplorer.Domain;
using MdExplorer.Platform.Abstractions;

namespace MdExplorer.Application;

/// <summary>自动化图校验结果；有效时同时给出从来源开始的确定性执行顺序。</summary>
public sealed record AutomationValidation(
    IReadOnlyList<string> Errors,
    IReadOnlyList<AutomationNodeDefinition> OrderedNodes)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// 自动化一次执行的聚合结果。文件操作结果与系统通知结果刻意分开：通知权限或桌面服务失败
/// 不得把已经落盘的复制/移动误报成失败，反过来文件失败时仍可按流程发送失败摘要。
/// </summary>
public sealed record AutomationExecutionResult(
    OperationOutcome Operation,
    IReadOnlyList<FileItem> MatchedFiles,
    string? DestinationPath,
    SystemNotificationResult? Notification,
    IReadOnlyList<string> ValidationErrors);

/// <summary>
/// 自动化线性流水线的纯校验器。v0.21 只接受“当前文件夹 → 零或多个筛选 → 一次传输 →
/// 可选系统通知”的单链；这既覆盖第一批整理场景，也避免让预览版节点画布承载任意
/// 分支、循环与脚本的不可预测写操作。
/// </summary>
public static class AutomationWorkflowValidator
{
    public static AutomationValidation Validate(AutomationWorkflowDefinition? workflow)
    {
        var errors = new List<string>();
        if (workflow is null)
            return new AutomationValidation(["工作流为空。"], []);
        if (string.IsNullOrWhiteSpace(workflow.Name))
            errors.Add("工作流名称不能为空。");
        else if (workflow.Name.Trim().Length > 80)
            errors.Add("工作流名称过长（上限 80 字符）。");

        var nodes = workflow.Nodes ?? [];
        var connections = workflow.Connections ?? [];
        if (nodes.Count == 0)
        {
            errors.Add("工作流至少需要“当前文件夹”和“传输到子文件夹”两个节点。");
            return new AutomationValidation(errors, []);
        }

        var nodeById = new Dictionary<string, AutomationNodeDefinition>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.Id))
            {
                errors.Add("每个节点都必须有非空 ID。");
                continue;
            }
            if (!nodeById.TryAdd(node.Id, node))
                errors.Add($"节点 ID 重复：{node.Id}。");
        }
        if (nodeById.Count != nodes.Count)
            errors.Add("节点集合包含空节点、空 ID 或重复 ID。");

        var sourceNodes = nodes.Where(node => node?.Kind == AutomationNodeKind.CurrentFolder).ToArray();
        var transferNodes = nodes.Where(node => node?.Kind == AutomationNodeKind.TransferToSubfolder).ToArray();
        var notificationNodes = nodes.Where(node => node?.Kind == AutomationNodeKind.SystemNotification).ToArray();
        if (sourceNodes.Length != 1)
            errors.Add("工作流必须且只能包含一个“当前文件夹”节点。");
        if (transferNodes.Length != 1)
            errors.Add("工作流必须且只能包含一个“传输到子文件夹”节点。");
        if (notificationNodes.Length > 1)
            errors.Add("工作流最多只能包含一个“系统通知”节点。");

        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (!Enum.IsDefined(node.Kind))
            {
                errors.Add($"节点 {node.Id} 的类型未知。");
                continue;
            }

            switch (node.Kind)
            {
                case AutomationNodeKind.KindFilter:
                    if (node.FilterKind == FileKind.Folder)
                        errors.Add("“当前文件夹”来源只处理顶层文件，类型筛选不能选择文件夹。");
                    else if (node.FilterKind is { } filterKind && !Enum.IsDefined(filterKind))
                        errors.Add("类型筛选包含未知文件类型。");
                    break;
                case AutomationNodeKind.NameContains:
                    if ((node.NameQuery ?? string.Empty).Length > 200)
                        errors.Add("名称筛选过长（上限 200 字符）。");
                    break;
                case AutomationNodeKind.TransferToSubfolder:
                    var destinationError = NameValidator.Validate(node.DestinationSubfolder?.Trim());
                    if (destinationError is not null)
                        errors.Add($"目标子文件夹名称不合法：{destinationError}。");
                    if (!Enum.IsDefined(node.TransferMode))
                        errors.Add("传输节点包含未知模式。");
                    break;
                case AutomationNodeKind.SystemNotification:
                    ValidateNotificationText(node.NotificationTitle, "通知标题", 120, requireNonEmpty: true, errors);
                    ValidateNotificationText(node.NotificationBody, "通知正文", 500, requireNonEmpty: false, errors);
                    break;
            }
        }

        var outgoing = new Dictionary<string, string>(StringComparer.Ordinal);
        var incoming = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenPairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            if (connection is null ||
                string.IsNullOrWhiteSpace(connection.SourceNodeId) ||
                string.IsNullOrWhiteSpace(connection.TargetNodeId))
            {
                errors.Add("连线两端都必须引用节点。");
                continue;
            }
            if (!nodeById.ContainsKey(connection.SourceNodeId) || !nodeById.ContainsKey(connection.TargetNodeId))
            {
                errors.Add($"连线引用了不存在的节点：{connection.SourceNodeId} → {connection.TargetNodeId}。");
                continue;
            }
            if (connection.SourceNodeId == connection.TargetNodeId)
            {
                errors.Add("节点不能连接到自身。");
                continue;
            }
            if (!seenPairs.Add($"{connection.SourceNodeId}\n{connection.TargetNodeId}"))
            {
                errors.Add($"连线重复：{connection.SourceNodeId} → {connection.TargetNodeId}。");
                continue;
            }
            if (!outgoing.TryAdd(connection.SourceNodeId, connection.TargetNodeId))
                errors.Add($"节点 {connection.SourceNodeId} 最多只能有一条出线。");
            if (!incoming.TryAdd(connection.TargetNodeId, connection.SourceNodeId))
                errors.Add($"节点 {connection.TargetNodeId} 最多只能有一条入线。");
        }

        var ordered = new List<AutomationNodeDefinition>();
        if (sourceNodes.Length == 1 && sourceNodes[0] is { } source)
        {
            if (incoming.ContainsKey(source.Id))
                errors.Add("“当前文件夹”必须是流水线起点，不能有入线。");

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = source;
            while (current is not null)
            {
                if (!visited.Add(current.Id))
                {
                    errors.Add("工作流包含循环连线。");
                    break;
                }
                ordered.Add(current);
                current = outgoing.TryGetValue(current.Id, out var nextId) &&
                          nodeById.TryGetValue(nextId, out var next)
                    ? next
                    : null;
            }
            if (ordered.Count != nodeById.Count)
                errors.Add("所有节点都必须连接在同一条从“当前文件夹”开始的流水线上。");
        }

        if (ordered.Count > 0 && transferNodes.Length == 1 && transferNodes[0] is { } transfer)
        {
            var transferIndex = ordered.FindIndex(node => node.Id == transfer.Id);
            if (transferIndex < 1)
            {
                errors.Add("传输节点必须连接在“当前文件夹”之后。");
            }
            else
            {
                for (var i = 1; i < transferIndex; i++)
                {
                    if (ordered[i].Kind is not (AutomationNodeKind.KindFilter or AutomationNodeKind.NameContains))
                        errors.Add("传输节点之前只允许放置类型或名称筛选。");
                }

                var tailCount = ordered.Count - transferIndex - 1;
                if (tailCount > 1 || (tailCount == 1 && ordered[^1].Kind != AutomationNodeKind.SystemNotification))
                    errors.Add("传输节点之后只允许一个终点“系统通知”节点。");
            }
        }

        return errors.Count == 0
            ? new AutomationValidation([], ordered)
            : new AutomationValidation(errors.Distinct().ToArray(), []);
    }

    internal static void ValidateNotificationText(
        string? value,
        string label,
        int maxLength,
        bool requireNonEmpty,
        List<string> errors)
    {
        var text = value ?? string.Empty;
        if (requireNonEmpty && string.IsNullOrWhiteSpace(text))
            errors.Add($"{label}不能为空。");
        if (text.Length > maxLength)
            errors.Add($"{label}过长（上限 {maxLength} 字符）。");

        var offset = 0;
        while (offset < text.Length)
        {
            var open = text.IndexOf('{', offset);
            if (open < 0)
                break;
            var close = text.IndexOf('}', open + 1);
            if (close < 0)
            {
                errors.Add($"{label}包含未闭合的占位符。");
                break;
            }

            var placeholder = text[(open + 1)..close].Trim();
            if (!placeholder.Equals("count", StringComparison.OrdinalIgnoreCase) &&
                !placeholder.Equals("succeeded", StringComparison.OrdinalIgnoreCase) &&
                !placeholder.Equals("failed", StringComparison.OrdinalIgnoreCase) &&
                !placeholder.Equals("destination", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{label}包含未知占位符：{{{placeholder}}}。仅支持 {{count}}、{{succeeded}}、{{failed}}、{{destination}}。");
            }
            offset = close + 1;
        }
    }
}

/// <summary>
/// 自动化执行器。来源固定为运行时的活动浏览器文件夹；仅枚举一层文件、只在有匹配时
/// 创建目标夹，并以 KeepBoth 复用既有安全传输管线，节点编辑器不得绕过任务中心或覆盖文件。
/// </summary>
public sealed class AutomationWorkflowExecutor
{
    public const int MaxTopLevelFiles = 100_000;

    private readonly IFileSystemService _fileSystem;
    private readonly IFileOperationService _operations;
    private readonly ISystemNotificationService _notifications;

    public AutomationWorkflowExecutor(
        IFileSystemService fileSystem,
        IFileOperationService operations,
        ISystemNotificationService notifications)
    {
        _fileSystem = fileSystem;
        _operations = operations;
        _notifications = notifications;
    }

    public async Task<AutomationExecutionResult> ExecuteAsync(
        AutomationWorkflowDefinition workflow,
        string sourceFolder,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var validation = AutomationWorkflowValidator.Validate(workflow);
        if (!validation.IsValid)
        {
            return Failed(
                validation.Errors.Select(error => new OperationError("自动化工作流", error)).ToArray(),
                [],
                null,
                validation.Errors);
        }

        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            return Failed(
                [new OperationError("当前文件夹", "尚未打开可运行自动化的文件夹。")],
                [],
                null,
                []);
        }
        if (FileLocator.IsStorageUri(sourceFolder))
        {
            return Failed(
                [new OperationError("当前文件夹", "自动化第一版仅支持本地文件夹，暂不支持 Android 存储授权目录（SAF）。")],
                [],
                null,
                []);
        }
        if (!Path.IsPathFullyQualified(sourceFolder))
        {
            return Failed(
                [new OperationError("当前文件夹", "自动化来源必须是本地绝对路径。")],
                [],
                null,
                []);
        }

        var files = new List<FileItem>();
        try
        {
            await foreach (var item in _fileSystem.ListAsync(sourceFolder, false, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.IsFolder)
                    continue;
                files.Add(item);
                if (files.Count > MaxTopLevelFiles)
                {
                    return Failed(
                        [new OperationError("当前文件夹", $"顶层文件超过安全上限 {MaxTopLevelFiles}，已停止且未传输。", sourceFolder)],
                        [],
                        null,
                        []);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AutomationExecutionResult(
                new OperationOutcome(0, [], [], Cancelled: true), [], null, null, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return Failed(
                [new OperationError(Path.GetFileName(sourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), $"枚举当前文件夹失败：{ex.Message}", sourceFolder)],
                [],
                null,
                []);
        }

        var ordered = validation.OrderedNodes;
        var transferNode = ordered.First(node => node.Kind == AutomationNodeKind.TransferToSubfolder);
        var notificationNode = ordered.FirstOrDefault(node => node.Kind == AutomationNodeKind.SystemNotification);
        var matched = files.Where(file => ordered
            .TakeWhile(node => node.Kind != AutomationNodeKind.TransferToSubfolder)
            .All(node => Matches(file, node))).ToArray();

        if (matched.Length == 0)
        {
            progress?.Report(new OperationProgress(1, "没有匹配文件", 0, 0));
            var emptyOutcome = new OperationOutcome(0, [], []);
            var emptyNotification = await TryNotifyAsync(
                notificationNode, emptyOutcome, matched.Length, null, cancellationToken).ConfigureAwait(false);
            return new AutomationExecutionResult(emptyOutcome, matched, null, emptyNotification, []);
        }

        string destinationPath;
        try
        {
            destinationPath = await _fileSystem.CreateFolderAsync(
                sourceFolder,
                transferNode.DestinationSubfolder.Trim(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AutomationExecutionResult(
                new OperationOutcome(0, [], [], Cancelled: true), matched, null, null, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return Failed(
                [new OperationError(transferNode.DestinationSubfolder.Trim(), $"创建目标子文件夹失败：{ex.Message}", sourceFolder)],
                matched,
                null,
                []);
        }

        progress?.Report(new OperationProgress(0, "正在传输匹配文件", 0, matched.Length));
        OperationOutcome outcome;
        try
        {
            var sources = matched.Select(file => file.FullPath).ToArray();
            outcome = transferNode.TransferMode == AutomationTransferMode.Move
                ? await _operations.MoveAsync(sources, destinationPath, ConflictPolicy.KeepBoth, progress, cancellationToken).ConfigureAwait(false)
                : await _operations.CopyAsync(sources, destinationPath, ConflictPolicy.KeepBoth, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AutomationExecutionResult(
                new OperationOutcome(0, [], [], Cancelled: true), matched, destinationPath, null, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return Failed(
                [new OperationError("传输文件", ex.Message, sourceFolder)],
                matched,
                destinationPath,
                []);
        }

        if (outcome.Cancelled)
            return new AutomationExecutionResult(outcome, matched, destinationPath, null, []);

        var notification = await TryNotifyAsync(
            notificationNode, outcome, matched.Length, destinationPath, cancellationToken).ConfigureAwait(false);
        return new AutomationExecutionResult(outcome, matched, destinationPath, notification, []);
    }

    public static string FormatNotificationTemplate(
        string? template,
        int count,
        int succeeded,
        int failed,
        string? destination)
    {
        var text = template ?? string.Empty;
        return text
            .Replace("{count}", count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{succeeded}", succeeded.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{failed}", failed.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{destination}", destination ?? "未创建目标文件夹", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(FileItem file, AutomationNodeDefinition node) => node.Kind switch
    {
        AutomationNodeKind.KindFilter => node.FilterKind is null || file.Kind == node.FilterKind.Value,
        AutomationNodeKind.NameContains => string.IsNullOrWhiteSpace(node.NameQuery) ||
            file.Name.Contains(node.NameQuery.Trim(), StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    private async Task<SystemNotificationResult?> TryNotifyAsync(
        AutomationNodeDefinition? node,
        OperationOutcome outcome,
        int matchedCount,
        string? destinationPath,
        CancellationToken cancellationToken)
    {
        if (node is null || outcome.Cancelled)
            return null;

        var failed = Math.Max(0, outcome.Errors.Count + outcome.SkippedCount);
        var title = FormatNotificationTemplate(
            node.NotificationTitle, matchedCount, outcome.SucceededCount, failed, destinationPath);
        var body = FormatNotificationTemplate(
            node.NotificationBody, matchedCount, outcome.SucceededCount, failed, destinationPath);
        try
        {
            return await _notifications.ShowAsync(title, body, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return SystemNotificationResult.Failed(ex.Message);
        }
    }

    private static AutomationExecutionResult Failed(
        IReadOnlyList<OperationError> errors,
        IReadOnlyList<FileItem> matchedFiles,
        string? destinationPath,
        IReadOnlyList<string> validationErrors) => new(
        new OperationOutcome(0, errors, []),
        matchedFiles,
        destinationPath,
        null,
        validationErrors);
}
