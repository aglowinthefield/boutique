using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReactiveUI;
using RequiemGlamPatcher.Models;

namespace RequiemGlamPatcher.ViewModels;

public class DistributionFileViewModel : ReactiveObject
{
    private readonly DistributionFile _file;

    public DistributionFileViewModel(DistributionFile file)
    {
        _file = file;
    }

    public string FileName => _file.FileName;
    public string RelativePath => _file.RelativePath;
    public string Directory => Path.GetDirectoryName(_file.RelativePath) ?? string.Empty;
    public string FullPath => _file.FullPath;
    public IReadOnlyList<DistributionLine> Lines => _file.Lines;

    public string TypeDisplay => _file.Type switch
    {
        DistributionFileType.Spid => "SPID",
        DistributionFileType.SkyPatcher => "SkyPatcher",
        _ => _file.Type.ToString()
    };

    public int RecordCount => _file.Lines.Count(l => l.Kind == DistributionLineKind.KeyValue);
    public int CommentCount => _file.Lines.Count(l => l.Kind == DistributionLineKind.Comment);
}
