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

    [Theory]
    [InlineData("ADD_STAGE_SKILL_COLUMN_SQL")]
    [InlineData("ADD_MERGE_COLUMNS_SQL")]
    [InlineData("ADD_CONFLICT_COLUMNS_SQL")]
    public void LegacyColumnMigrations_OnlyActWhenTheOldTableExists(string ConstantName)
    {
        // Usan los nombres antiguos de tabla. En una base nueva (o ya renombrada) esa tabla no existe y, sin esta
        // guarda, el ALTER TABLE fallaría al arrancar.
        var sql = (string)typeof(DbInitializer)
            .GetField(ConstantName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetRawConstantValue()!;

        var alters = Regex.Matches(sql, "ALTER TABLE \"(\\w+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(alters);
        foreach (var table in alters)
        {
            Assert.Matches(
                $@"IF EXISTS \(SELECT 1 FROM information_schema\.tables\s+WHERE table_schema = current_schema\(\) AND table_name = '{table}'\)",
                sql);
        }
        Assert.Contains("NOT EXISTS (SELECT 1 FROM information_schema.columns", sql);
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

    [Fact]
    public void CaptureColumnSql_UsesTheModuleTableName_AndHasNoBracesThatEfCoreWouldTreatAsPlaceholders()
    {
        // Corre después del renombrado: una base que ya usa los nombres por módulo también necesita la columna.
        var sql = DbInitializer.ADD_CAPTURE_COLUMN_SQL;

        Assert.Contains($"table_name = '{TableNames.TASK_ATTACHMENTS}' AND column_name = 'IsCapture'", sql);
        // Sin la tabla no hay nada que alterar: el ALTER no debe fallar si cambia el orden de las adaptaciones.
        Assert.Contains("FROM information_schema.tables", sql);
        Assert.Contains($"ALTER TABLE \"{TableNames.TASK_ATTACHMENTS}\" ADD COLUMN \"IsCapture\"", sql);
        Assert.DoesNotContain("table_name = 'TaskAttachments'", sql);
        Assert.DoesNotContain("{", sql);
        Assert.DoesNotContain("}", sql);
    }
}
