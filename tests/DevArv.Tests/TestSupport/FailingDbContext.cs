using DevArv.Web.DatabaseConnection;
using Microsoft.EntityFrameworkCore;

namespace DevArv.Tests.TestSupport;

/// <summary>
/// Contexto que lanza <see cref="DbUpdateException"/> en el guardado número <see cref="FailOnSave"/> (1 = el primero).
/// Sirve para probar qué hace el código cuando la base de datos rechaza la operación.
/// </summary>
public sealed class FailingDbContext(DbContextOptions<AppDbContext> Options, int FailOnSave) : AppDbContext(Options)
{
    private int _Saves;

    public override Task<int> SaveChangesAsync(CancellationToken CT = default)
    {
        if (++_Saves == FailOnSave) throw new DbUpdateException("Fallo simulado de la base de datos.");
        return base.SaveChangesAsync(CT);
    }
}
