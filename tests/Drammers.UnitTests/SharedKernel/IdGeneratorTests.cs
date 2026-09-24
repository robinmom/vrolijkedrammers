using Drammers.SharedKernel.Identifiers;

namespace Drammers.UnitTests.SharedKernel;

public class IdGeneratorTests
{
    [Fact]
    public void NewId_levert_versie_7_uuids()
    {
        var id = IdGenerator.NewId();

        Assert.Equal(7, id.Version);
    }

    [Fact]
    public void NewId_is_oplopend_in_de_tijd()
    {
        var first = IdGenerator.NewId();
        Thread.Sleep(2);
        var second = IdGenerator.NewId();

        Assert.True(string.CompareOrdinal(first.ToString(), second.ToString()) < 0);
    }
}
