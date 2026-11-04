namespace DevArv.Worker.Models;

public sealed record PreparedWorkspace(
    string WorktreePath,
    string AttachmentsPath,
    string StartCommit,
    bool RemoteBranchExisted);
