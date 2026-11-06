using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class SlugTests
{
    [Theory]
    [InlineData("Requerimientos de mejora para Fabric", "requerimientos-de-mejora-para-fabric")]
    [InlineData("  Añadir validación  ¡ya!  ", "anadir-validacion-ya")]
    [InlineData("###", "tarea")]
    public void Create_ProducesGitSafeSlug(string Input, string Expected) =>
        Assert.Equal(Expected, Slug.Create(Input));

    [Fact]
    public void Create_RespectsMaxLength() =>
        Assert.True(Slug.Create(new string('a', 200), 50).Length <= 50);
}
