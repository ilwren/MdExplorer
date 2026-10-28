using MdExplorer.Domain.Errors;
using MdExplorer.Domain.Operations;
using MdExplorer.Platform.Abstractions.Preview;

namespace MdExplorer.Platform.Abstractions.Archives;

/// 压缩包端口（§5.3/§6.6）：zip 全功能；7z/rar 仅元数据（降级）。
public interface IArchiveService
{
    Result<IReadOnlyList<ArchiveEntry>> ListChildren(string zipPath, string inner);
    Result<IReadOnlyList<ArchiveEntry>> ListAll(string zipPath);
    Result<byte[]> ReadEntry(string zipPath, string entryName);
    Result<int> Extract(string zipPath, IEnumerable<string>? entryNames, string destDir, bool keepStructure, ConflictPolicy policy);
    Result Compress(IEnumerable<string> paths, string zipPath);
}
