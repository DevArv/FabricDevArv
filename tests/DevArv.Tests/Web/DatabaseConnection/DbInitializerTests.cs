using System.Text.RegularExpressions;
using DevArv.Tests.TestSupport;
using DevArv.Web.DatabaseConnection;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Tests.Web.DatabaseConnection;

public class DbInitializerTests
{
    [Fact]
    public void TableRenames_TargetEveryTableOfTheModel_AndNothingElse()
    {
        using var db = TestDatabase.CreateContext(Guid.NewGuid().ToString("N"));

        var modelTables = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).ToHashSet();
        var targets = DbInitializer.TABLE_RENAMES.Select(r => r.NewName).ToHashSet();

        Assert.Equal(modelTables, targets);
    }

    [Fact]
    public void TableRenames_OldNamesAreTheOnesTheOldCodeUsed()
    {
        // Lista histórica escrita a mano: los nombres con los que se crearon las bases ya existentes.
        var historical = new[]
        {
            "Projects", "Stages", "Tasks", "TaskAttachments", "TaskRuns", "TaskComments", "TaskEvents", "Workers",
            "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens", "AspNetRoleClaims",
        };

        Assert.Equal(historical.Order(), DbInitializer.TABLE_RENAMES.Select(r => r.OldName).Order());
    }

    [Fact]
    public void RenameSql_RenamesEachTableOnlyWhenTheOldOneExistsAndTheNewOneDoesNot()
    {
        var sql = DbInitializer.BuildRenameTablesSql();

        foreach (var (oldName, newName) in DbInitializer.TABLE_RENAMES)
        {
            Assert.Contains($"ALTER TABLE \"{oldName}\" RENAME TO \"{newName}\";", sql);
            Assert.Contains($"table_name = '{oldName}'", sql);
            Assert.Contains($"table_name = '{newName}'", sql);
        }
        Assert.Equal(DbInitializer.TABLE_RENAMES.Length, Regex.Matches(sql, "NOT EXISTS").Count);
    }

    [Fact]
    public void RenameSql_IsOneAtomicBlock_WithoutBracesThatEfCoreWouldTreatAsPlaceholders()
    {
        var sql = DbInitializer.BuildRenameTablesSql();

        Assert.StartsWith("DO $$", sql);
        Assert.EndsWith("END $$;", sql);
        Assert.DoesNotContain("{", sql);
        Assert.DoesNotContain("}", sql);
    }
}
