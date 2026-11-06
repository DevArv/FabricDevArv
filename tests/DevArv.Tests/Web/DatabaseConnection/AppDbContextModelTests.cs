using DevArv.Tests.TestSupport;
using DevArv.Web.DataModels;
using DevArv.Web.DatabaseConnection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Tests.Web.DatabaseConnection;

public sealed class AppDbContextModelTests
{
    private readonly AppDbContext _Db = TestDatabase.CreateContext(Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(typeof(Project), "Projects_Projects")]
    [InlineData(typeof(Stage), "Workflow_Stages")]
    [InlineData(typeof(TaskItem), "Tasks_Tasks")]
    [InlineData(typeof(TaskAttachment), "Tasks_Attachments")]
    [InlineData(typeof(TaskRun), "Tasks_Runs")]
    [InlineData(typeof(TaskComment), "Tasks_Comments")]
    [InlineData(typeof(TaskEvent), "Tasks_Events")]
    [InlineData(typeof(WorkerNode), "Workers_Workers")]
    [InlineData(typeof(IdentityUser), "Identity_Users")]
    [InlineData(typeof(IdentityRole), "Identity_Roles")]
    [InlineData(typeof(IdentityUserRole<string>), "Identity_UserRoles")]
    [InlineData(typeof(IdentityUserClaim<string>), "Identity_UserClaims")]
    [InlineData(typeof(IdentityUserLogin<string>), "Identity_UserLogins")]
    [InlineData(typeof(IdentityUserToken<string>), "Identity_UserTokens")]
    [InlineData(typeof(IdentityRoleClaim<string>), "Identity_RoleClaims")]
    public void EveryTable_IsNamedModuleUnderscoreTable(Type Entity, string ExpectedTable) =>
        Assert.Equal(ExpectedTable, _Db.Model.FindEntityType(Entity)!.GetTableName());

    [Fact]
    public void EveryTable_FollowsTheModuleUnderscoreNameRule() =>
        Assert.All(_Db.Model.GetEntityTypes(), entity => Assert.Matches("^[A-Z][A-Za-z]+_[A-Z][A-Za-z]+$", entity.GetTableName()!));

    [Fact]
    public void EveryTableName_ComesFromTableNames()
    {
        var declared = typeof(TableNames).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToHashSet();

        Assert.All(_Db.Model.GetEntityTypes(), entity => Assert.Contains(entity.GetTableName()!, declared));
    }

    [Fact]
    public void DomainColumns_KeepTheirHistoricalNames_EvenThoughTheCodeSaysID()
    {
        var domain = _Db.Model.GetEntityTypes().Where(e => e.ClrType.Namespace == typeof(Project).Namespace).ToList();

        Assert.NotEmpty(domain);
        foreach (var entity in domain)
        {
            foreach (var property in entity.GetProperties())
                Assert.False(property.GetColumnName().EndsWith("ID", StringComparison.Ordinal),
                    $"{entity.ClrType.Name}.{property.Name} se guardaría en una columna «{property.GetColumnName()}».");
        }

        string Column(Type Entity, string Property) => _Db.Model.FindEntityType(Entity)!.FindProperty(Property)!.GetColumnName();
        Assert.Equal("Id", Column(typeof(TaskItem), nameof(TaskItem.ID)));
        Assert.Equal("ProjectId", Column(typeof(TaskItem), nameof(TaskItem.ProjectID)));
        Assert.Equal("CreatedById", Column(typeof(TaskItem), nameof(TaskItem.CreatedByID)));
        Assert.Equal("PreferredWorkerId", Column(typeof(TaskItem), nameof(TaskItem.PreferredWorkerID)));
        Assert.Equal("TaskId", Column(typeof(TaskRun), nameof(TaskRun.TaskID)));
        Assert.Equal("SessionId", Column(typeof(TaskRun), nameof(TaskRun.SessionID)));
        Assert.Equal("SuccessStageId", Column(typeof(Stage), nameof(Stage.SuccessStageID)));
        Assert.Equal("Title", Column(typeof(TaskItem), nameof(TaskItem.Title)));
    }

    [Fact]
    public void IdentityColumns_AreNotTouched() =>
        Assert.Equal("Id", _Db.Model.FindEntityType(typeof(IdentityUser))!.FindProperty(nameof(IdentityUser.Id))!.GetColumnName());

    [Theory]
    [InlineData("ID", "Id")]
    [InlineData("TaskID", "TaskId")]
    [InlineData("PreferredWorkerID", "PreferredWorkerId")]
    [InlineData("Title", "Title")]
    [InlineData("IDEA", "IDEA")]
    public void ToColumnName_OnlyChangesATrailingID(string Property, string Expected) =>
        Assert.Equal(Expected, AppDbContext.ToColumnName(Property));

    [Fact]
    public void EveryEnumProperty_UsesTheTextConverter()
    {
        var enumProperties = _Db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType.IsEnum)
            .ToList();

        Assert.NotEmpty(enumProperties);
        Assert.All(enumProperties, property =>
        {
            var converter = property.GetValueConverter();
            Assert.NotNull(converter);
            Assert.Equal(typeof(EnumNameStringConverter<>).MakeGenericType(property.ClrType), converter.GetType());
        });
    }
}
