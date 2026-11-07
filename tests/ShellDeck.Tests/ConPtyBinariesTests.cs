using System.IO;

namespace ShellDeck.Tests;

public sealed class ConPtyBinariesTests
{
    [Theory]
    [InlineData("conpty.dll")]
    [InlineData("OpenConsole.exe")]
    public void Conpty_and_OpenConsole_are_shipped_next_to_the_executable(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);

        Assert.True(File.Exists(path), $"{fileName} is missing from {AppContext.BaseDirectory}");
    }
}
