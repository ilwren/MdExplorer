using Avalonia.Controls;
using MdExplorer.Application;
using MdExplorer.Domain;
using MdExplorer.Infrastructure;
using MdExplorer.Platform.Abstractions;
using MdExplorer.UI.Services;
using MdExplorer.UI.ViewModels;
using MdExplorer.UI.Views;
using Xunit;

namespace MdExplorer.Tests;

/// <summary>v0.21 自动化单链校验、真实本地执行、通知隔离与 Shell 任务通知策略。</summary>
public sealed class AutomationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eidoria-automation-").FullName;
    private readonly string _dataDir;
    private readonly string _startDir;

    public AutomationTests()
    {
        _dataDir = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        _startDir = Directory.CreateDirectory(Path.Combine(_root, "start")).FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class FakeNotificationService : ISystemNotificationService
    {
        public SystemNotificationResult NextResult { get; set; } = SystemNotificationResult.Sent();
        public List<(string Title, string Body)> Sent { get; } = [];

        public string PlatformName => "Test";
        public string SupportDescription => "Test notification backend";

        public Task<SystemNotificationResult> ShowAsync(
            string title,
            string body,
            CancellationToken cancellationToken)
        {
            Sent.Add((title, body));
            return Task.FromResult(NextResult);
        }
    }

    private AutomationWorkflowExecutor CreateExecutor(ISystemNotificationService notifications) =>
        new(new LocalFileSystemService(), new FileOperationService(), notifications);

    private static AutomationWorkflowDefinition Workflow(
        FileKind? filterKind = FileKind.Image,
        AutomationTransferMode mode = AutomationTransferMode.Copy,
        string destination = "图片整理")
    {
        var workflow = AutomationWorkflowDefinition.CreateDefault();
        workflow.Nodes[1].FilterKind = filterKind;
        workflow.Nodes[2].TransferMode = mode;
        workflow.Nodes[2].DestinationSubfolder = destination;
        return workflow;
    }

    private string WriteStartFile(string name, string content = "x")
    {
        var path = Path.Combine(_startDir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData(true, WindowDecorations.Full)]
    [InlineData(false, WindowDecorations.BorderOnly)]
    public void MainWindowChromePolicy_OnlyWindowsKeepsNativeCaptionForDwmAnimation(
        bool isWindows,
        WindowDecorations expected)
    {
        Assert.Equal(expected, MainWindowChromePolicy.DecorationsForPlatform(isWindows));
    }

    [Fact]
    public void DefaultWorkflow_IsAValidLinearPipeline()
    {
        var validation = AutomationWorkflowValidator.Validate(AutomationWorkflowDefinition.CreateDefault());

        Assert.True(validation.IsValid, string.Join("；", validation.Errors));
        Assert.Equal(
            [
                AutomationNodeKind.CurrentFolder,
                AutomationNodeKind.KindFilter,
                AutomationNodeKind.TransferToSubfolder,
                AutomationNodeKind.SystemNotification,
            ],
            validation.OrderedNodes.Select(node => node.Kind).ToArray());
    }

    [Fact]
    public void Validator_RejectsDisconnectedAndUnsafeDefinitions()
    {
        var disconnected = AutomationWorkflowDefinition.CreateDefault();
        disconnected.Connections.RemoveAt(1);
        Assert.False(AutomationWorkflowValidator.Validate(disconnected).IsValid);

        var unsafeDestination = Workflow(destination: "../outside");
        var unsafeValidation = AutomationWorkflowValidator.Validate(unsafeDestination);
        Assert.False(unsafeValidation.IsValid);
        Assert.Contains(unsafeValidation.Errors, error => error.Contains("目标子文件夹", StringComparison.Ordinal));

        var folderFilter = Workflow(filterKind: FileKind.Folder);
        Assert.False(AutomationWorkflowValidator.Validate(folderFilter).IsValid);

        var unknownPlaceholder = AutomationWorkflowDefinition.CreateDefault();
        unknownPlaceholder.Nodes[3].NotificationBody = "处理了 {everything} 个文件";
        var placeholderValidation = AutomationWorkflowValidator.Validate(unknownPlaceholder);
        Assert.False(placeholderValidation.IsValid);
        Assert.Contains(placeholderValidation.Errors, error => error.Contains("everything", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Executor_CopiesOnlyMatchingTopLevelFiles_AndKeepsBoth()
    {
        var image = WriteStartFile("photo.png", "new-image");
        WriteStartFile("notes.txt", "text");
        WriteStartFile(Path.Combine("nested", "inner.jpg"), "nested-image");
        var destination = Directory.CreateDirectory(Path.Combine(_startDir, "图片整理")).FullName;
        File.WriteAllText(Path.Combine(destination, "photo.png"), "old-image");
        var notifications = new FakeNotificationService();

        var result = await CreateExecutor(notifications).ExecuteAsync(
            Workflow(), _startDir, null, TestContext.Current.CancellationToken);

        Assert.True(result.Operation.Success, string.Join("；", result.Operation.Errors.Select(e => e.Message)));
        Assert.Equal([image], result.MatchedFiles.Select(file => file.FullPath).ToArray());
        Assert.Equal("old-image", File.ReadAllText(Path.Combine(destination, "photo.png")));
        Assert.Equal("new-image", File.ReadAllText(Path.Combine(destination, "photo (2).png")));
        var sent = Assert.Single(notifications.Sent);
        Assert.Contains("1/1", sent.Body, StringComparison.Ordinal);
        Assert.Contains(destination, sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Executor_NoMatches_DoesNotCreateEmptyDestination_ButStillNotifies()
    {
        WriteStartFile("notes.txt");
        var notifications = new FakeNotificationService();

        var result = await CreateExecutor(notifications).ExecuteAsync(
            Workflow(filterKind: FileKind.Video, destination: "视频整理"),
            _startDir,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.Operation.Success);
        Assert.Empty(result.MatchedFiles);
        Assert.Null(result.DestinationPath);
        Assert.False(Directory.Exists(Path.Combine(_startDir, "视频整理")));
        var sent = Assert.Single(notifications.Sent);
        Assert.Contains("0/0", sent.Body, StringComparison.Ordinal);
        Assert.Contains("未创建目标文件夹", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Executor_NotificationFailure_DoesNotTurnSuccessfulMoveIntoFailure()
    {
        var source = WriteStartFile("clip.mp4", "video");
        var notifications = new FakeNotificationService
        {
            NextResult = SystemNotificationResult.PermissionDenied("denied"),
        };

        var result = await CreateExecutor(notifications).ExecuteAsync(
            Workflow(FileKind.Video, AutomationTransferMode.Move, "视频整理"),
            _startDir,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.Operation.Success);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(_startDir, "视频整理", "clip.mp4")));
        Assert.Equal(SystemNotificationStatus.PermissionDenied, result.Notification?.Status);
    }

    [Fact]
    public async Task Executor_RejectsStorageUriSource()
    {
        var notifications = new FakeNotificationService();

        var result = await CreateExecutor(notifications).ExecuteAsync(
            Workflow(),
            "content://com.android.externalstorage.documents/tree/root",
            null,
            TestContext.Current.CancellationToken);

        Assert.False(result.Operation.Success);
        Assert.Contains(result.Operation.Errors, error => error.Message.Contains("SAF", StringComparison.Ordinal));
        Assert.Empty(notifications.Sent);
    }

    [Fact]
    public async Task Settings_RoundTripPreservesAutomationWorkflowAndTaskNotificationToggle()
    {
        var store = await SettingsStore.LoadAsync(_dataDir, TestContext.Current.CancellationToken);
        store.Settings.NotifyOnTaskCompletion = true;
        store.Settings.AutomationWorkflow.Name = "收纳截图";
        store.Settings.AutomationWorkflow.Nodes[2].DestinationSubfolder = "截图归档";
        store.Settings.AutomationWorkflow.Nodes[1].X = 321.5;
        await store.SaveSettingsAsync(TestContext.Current.CancellationToken);

        var reloaded = await SettingsStore.LoadAsync(_dataDir, TestContext.Current.CancellationToken);

        Assert.True(reloaded.Settings.NotifyOnTaskCompletion);
        Assert.Equal("收纳截图", reloaded.Settings.AutomationWorkflow.Name);
        Assert.Equal("截图归档", reloaded.Settings.AutomationWorkflow.Nodes[2].DestinationSubfolder);
        Assert.Equal(321.5, reloaded.Settings.AutomationWorkflow.Nodes[1].X);
        Assert.Equal(3, reloaded.Settings.AutomationWorkflow.Connections.Count);
    }

    [Fact]
    public async Task ShellAutomation_RunsThroughTaskCenter_AndRefreshesBrowser()
    {
        WriteStartFile("cover.jpg", "image");
        WriteStartFile("readme.txt", "text");
        var notifications = new FakeNotificationService();
        var shell = AppHost.CreateShell(AppHost.Build(
            dataDirectory: _dataDir,
            startFolder: _startDir,
            systemNotifications: notifications));
        shell.Browser.SetWatchEnabled(false);
        await shell.Browser.NavigateToAsync(_startDir, pushHistory: false);

        shell.GoAutomationCommand.Execute(null);
        Assert.Equal(ShellPage.Automation, shell.Page);
        Assert.True(shell.Automation.IsWorkflowValid, shell.Automation.ValidationText);

        await shell.Automation.RunCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(_startDir, "图片整理", "cover.jpg")));
        Assert.Single(notifications.Sent);
        Assert.Contains(shell.Browser.Items, item => item.Name == "图片整理");
        var task = Assert.Single(shell.OperationTasks);
        Assert.True(task.IsCompleted);
        Assert.False(task.IsFailed);
    }

    [Fact]
    public async Task AutomationEditor_AddReconnectPersistAndDeleteNode()
    {
        var notifications = new FakeNotificationService();
        var shell = AppHost.CreateShell(AppHost.Build(
            dataDirectory: _dataDir,
            startFolder: _startDir,
            systemNotifications: notifications));
        shell.Browser.SetWatchEnabled(false);
        var automation = shell.Automation;

        automation.AddNameFilterNodeCommand.Execute(null);
        var nodes = automation.Nodes.OfType<AutomationNodeViewModel>().ToArray();
        var source = Assert.Single(nodes, node => node.IsCurrentFolder);
        var kind = Assert.Single(nodes, node => node.IsKindFilter);
        var name = Assert.Single(nodes, node => node.IsNameFilter);
        var transfer = Assert.Single(nodes, node => node.IsTransfer);
        Assert.Same(name, automation.SelectedNode);
        Assert.False(automation.IsWorkflowValid); // 新节点尚未连入单链

        name.NameQuery = "IMG_";
        var kindToTransfer = Assert.Single(
            automation.Connections,
            connection => ReferenceEquals(connection.Source, kind.OutputConnector));
        automation.Disconnect(kindToTransfer);
        automation.Connect(kind.OutputConnector!, name.InputConnector!);
        automation.Connect(name.OutputConnector!, transfer.InputConnector!);

        Assert.True(automation.IsWorkflowValid, automation.ValidationText);
        Assert.Equal(4, automation.Connections.Count);
        Assert.Equal(5, shell.SettingsStore.Settings.AutomationWorkflow.Nodes.Count);

        // 单链不允许再从来源拉第二条出线到传输节点。
        automation.Connect(source.OutputConnector!, transfer.InputConnector!);
        Assert.Equal(4, automation.Connections.Count);
        Assert.Contains("出线", automation.StatusText, StringComparison.Ordinal);

        await shell.SettingsStore.SaveSettingsAsync(TestContext.Current.CancellationToken);
        var restored = AppHost.CreateShell(AppHost.Build(
            dataDirectory: _dataDir,
            startFolder: _startDir,
            systemNotifications: notifications));
        restored.Browser.SetWatchEnabled(false);
        var restoredNodes = restored.Automation.Nodes.OfType<AutomationNodeViewModel>().ToArray();
        var restoredName = Assert.Single(restoredNodes, node => node.IsNameFilter);
        Assert.Equal(5, restoredNodes.Length);
        Assert.Equal("IMG_", restoredName.NameQuery);
        Assert.True(restored.Automation.IsWorkflowValid, restored.Automation.ValidationText);

        restored.Automation.SelectedNodes.Clear();
        restored.Automation.SelectedNodes.Add(restoredName);
        Assert.Same(restoredName, restored.Automation.SelectedNode);
        restored.Automation.DeleteSelectedNodeCommand.Execute(null);
        Assert.Equal(4, restored.Automation.Nodes.Count);
        Assert.False(restored.Automation.IsWorkflowValid); // 删除中间节点后必须由用户重新连线
    }

    [Fact]
    public async Task ShellTaskNotifications_SkipShortSuccess_ButReportFailure()
    {
        var notifications = new FakeNotificationService();
        var shell = AppHost.CreateShell(AppHost.Build(
            dataDirectory: _dataDir,
            startFolder: _startDir,
            systemNotifications: notifications));
        shell.NotifyOnTaskCompletion = true;

        await shell.RunOperationAsync(
            "短成功",
            (_, _) => Task.FromResult(new OperationOutcome(1, [], [])));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(notifications.Sent);

        await shell.RunOperationAsync(
            "失败任务",
            (_, _) => Task.FromResult(new OperationOutcome(
                0,
                [new OperationError("a.txt", "denied")],
                [])));
        await WaitForAsync(() => notifications.Sent.Count == 1);

        var sent = Assert.Single(notifications.Sent);
        Assert.Equal("Eidoria 任务未完全完成", sent.Title);
        Assert.Contains("失败任务", sent.Body, StringComparison.Ordinal);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
        Assert.True(condition());
    }
}
