using DevArv.Web.DataModels;

namespace DevArv.Web.ViewModels;

public sealed record ProjectsRowViewModel(Project Project, int OpenTasks, decimal Cost);
