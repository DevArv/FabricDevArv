using DevArv.Web.DatabaseConnection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DevArv.Tests.TestSupport;

/// <summary>Base de datos en memoria para las pruebas de Repositories y Services.</summary>
public static class TestDatabase
{
    public static DbContextOptions<AppDbContext> CreateOptions(string Name) => new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Name)
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;

    public static AppDbContext CreateContext(string Name) => new(CreateOptions(Name));
}
