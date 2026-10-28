using MdExplorer.UI.ViewModels;
using MdExplorer.UI.Views;

namespace MdExplorer.UI.Services;

/// UI 组装引导：显式注册视图映射（§4.3 禁反射 ViewLocator）。
public static class UiBoot
{
    public static ViewFactory CreateViewFactory()
        => new ViewFactory()
            .Register<BrowserViewModel>(() => new BrowserView())
            .Register<SearchCenterViewModel>(() => new SearchCenterView())
            .Register<SettingsViewModel>(() => new SettingsView())
            .Register<HomeViewModel>(() => new HomeView())
            .Register<TasksViewModel>(() => new TasksView())
            .Register<TrashViewModel>(() => new TrashView())
            .Register<InsightsViewModel>(() => new InsightsView())
            .Register<ArchiveViewModel>(() => new ArchiveView());
}
