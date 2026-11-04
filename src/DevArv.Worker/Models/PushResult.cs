using DevArv.Contracts.Models;

namespace DevArv.Worker.Models;

public sealed record PushResult(string HeadCommit, bool Pushed, IReadOnlyList<ChangedFile> ChangedFiles);
