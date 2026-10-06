namespace NaraDreamPainter.Tests;

internal static class TestFiles
{
    /// <summary>
    /// Reads a source file, riding out the moment a concurrent build holds it open. Everything under
    /// src/ is also being compiled while the suite runs, and a locked file should not read as a
    /// repository problem.
    /// </summary>
    internal static string ReadText(string path) => Retry(() => File.ReadAllText(path));

    internal static string[] ReadLines(string path) => Retry(() => File.ReadAllLines(path));

    private static T Retry<T>(Func<T> read)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return read();
            }
            catch (IOException) when (attempt < 6)
            {
                Thread.Sleep(200);
            }
        }
    }
}
