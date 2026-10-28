using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.FileSystem;

namespace MdExplorer.Infrastructure.FileSystem;

/// 平台路径规则（§5.2）。桌面实现按本地文件系统规则；Android 实现按 SAF 规则。
public sealed class LocalPathService : IPathService
{
    public FileLocator? GetParent(FileLocator locator)
    {
        if (!locator.IsLocalPath) return null;
        var parent = Path.GetDirectoryName(locator.ToLocalPath());
        return string.IsNullOrEmpty(parent) ? null : FileLocator.FromPath(parent);
    }

    public FileLocator Combine(FileLocator folder, string name)
        => FileLocator.FromPath(Path.Combine(folder.ToLocalPath(), name));

    public string GetFileName(FileLocator locator)
        => locator.IsLocalPath ? Path.GetFileName(locator.ToLocalPath()) : locator.Value;

    public string? GetExtension(FileLocator locator)
        => locator.IsLocalPath ? Path.GetExtension(locator.ToLocalPath()) : null;

    public string Describe(FileLocator locator)
    {
        if (!locator.IsLocalPath) return locator.Value;
        var path = locator.ToLocalPath();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && path.StartsWith(home, StringComparison.Ordinal)
            ? "~" + path[home.Length..]
            : path;
    }
}

/// 卷与已知目录（§5.2）：UI 永不自己推导目录在哪。
public sealed class LocalVolumeService : IVolumeService
{
    public event EventHandler? VolumesChanged;

    public ValueTask<IReadOnlyList<StorageVolume>> GetVolumesAsync(CancellationToken ct)
    {
        var list = new List<StorageVolume>();
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            list.Add(new StorageVolume(
                "home", "主目录", FileLocator.FromPath(home),
                null, null, IsRemovable: false, IsPrimary: true));
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var root = drive.RootDirectory.FullName;
                if (root == home || home.StartsWith(root, StringComparison.Ordinal)) continue;
                list.Add(new StorageVolume(
                    root,
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? root : drive.VolumeLabel,
                    FileLocator.FromPath(root),
                    drive.TotalSize,
                    drive.AvailableFreeSpace,
                    drive.DriveType == DriveType.Removable,
                    false));
            }
        }
        catch
        {
            // 卷探测失败时至少给出主目录
        }
        return ValueTask.FromResult<IReadOnlyList<StorageVolume>>(list);
    }
}

public sealed class LocalKnownFoldersService : IKnownFoldersService
{
    public ValueTask<FileLocator?> GetAsync(KnownFolder folder, CancellationToken ct)
    {
        var special = folder switch
        {
            KnownFolder.Home => Environment.SpecialFolder.UserProfile,
            KnownFolder.Documents => Environment.SpecialFolder.MyDocuments,
            KnownFolder.Downloads => Environment.SpecialFolder.UserProfile,
            KnownFolder.Pictures => Environment.SpecialFolder.MyPictures,
            KnownFolder.Music => Environment.SpecialFolder.MyMusic,
            KnownFolder.Videos => Environment.SpecialFolder.MyVideos,
            KnownFolder.Desktop => Environment.SpecialFolder.Desktop,
            KnownFolder.AppData => Environment.SpecialFolder.ApplicationData,
            KnownFolder.Cache => Environment.SpecialFolder.LocalApplicationData,
            _ => (Environment.SpecialFolder)(-1),
        };
        if (special == (Environment.SpecialFolder)(-1))
            return ValueTask.FromResult<FileLocator?>(null);
        var path = Environment.GetFolderPath(special);
        if (folder == KnownFolder.Downloads)
        {
            var dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(dl)) path = dl;
        }
        return string.IsNullOrEmpty(path) || !Directory.Exists(path)
            ? ValueTask.FromResult<FileLocator?>(null)
            : ValueTask.FromResult<FileLocator?>(FileLocator.FromPath(path));
    }
}
