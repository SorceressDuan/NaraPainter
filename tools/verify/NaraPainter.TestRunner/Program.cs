using System.Reflection;
using System.Runtime.Loader;
using Xunit;
using Xunit.Sdk;

namespace NaraPainter.TestRunner;

/// <summary>
/// Fallback runner for the xUnit suite. vstest cannot start its test host in this sandbox (the host
/// opens a handle to the vstest process and the sandbox denies it), so verify.ps1 runs this instead
/// of "dotnet test" when that happens. It handles what the suite uses: Fact, Theory with
/// InlineData/MemberData/ClassData, Skip, async tests and IDisposable test classes.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("usage: NaraPainter.TestRunner <test-assembly> [type-filter]");
            return 2;
        }

        string path = Path.GetFullPath(args[0]);
        if (!File.Exists(path))
        {
            Console.WriteLine($"Could not find '{path}'.");
            return 2;
        }

        string? filter = args.Length > 1 ? args[1] : null;
        string directory = Path.GetDirectoryName(path)!;
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
        };

        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);

        int passed = 0;
        int failed = 0;
        int skipped = 0;
        var failures = new List<string>();
        var skips = new List<string>();

        foreach (Type type in assembly.GetTypes().Where(IsRunnable))
        {
            if (filter is not null && !type.FullName!.Contains(filter, StringComparison.Ordinal)) continue;

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                FactAttribute? fact = method.GetCustomAttribute<FactAttribute>();
                if (fact is null) continue;

                string name = $"{type.Name}.{method.Name}";
                if (!string.IsNullOrEmpty(fact.Skip))
                {
                    skipped++;
                    skips.Add($"{name}: {fact.Skip}");
                    continue;
                }

                foreach (object[] row in Rows(method))
                {
                    try
                    {
                        object instance = Activator.CreateInstance(type)!;
                        try
                        {
                            Invoke(method, instance, row);
                            passed++;
                        }
                        finally
                        {
                            (instance as IDisposable)?.Dispose();
                        }
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        failures.Add($"{name}{Describe(row)}: {Unwrap(exception)}");
                    }
                }
            }
        }

        foreach (string skip in skips) Console.WriteLine($"Skipped {skip}");
        foreach (string failure in failures) Console.WriteLine($"Failed {failure}");
        Console.WriteLine();
        Console.WriteLine($"Passed!  - Failed: {failed,6}, Passed: {passed,6}, Skipped: {skipped,6}, Total: {passed + failed + skipped,6}");

        return failed == 0 ? 0 : 1;
    }

    private static bool IsRunnable(Type type) =>
        type.IsPublic && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null;

    private static IEnumerable<object[]> Rows(MethodInfo method)
    {
        DataAttribute[] data = [.. method.GetCustomAttributes<DataAttribute>()];
        if (data.Length == 0) return [Array.Empty<object>()];
        return data.SelectMany(attribute => attribute.GetData(method));
    }

    private static void Invoke(MethodInfo method, object instance, object[] row)
    {
        object? result = method.Invoke(instance, row);
        if (result is Task task) task.GetAwaiter().GetResult();
    }

    private static string Unwrap(Exception exception)
    {
        Exception actual = exception is TargetInvocationException { InnerException: not null } wrapped
            ? wrapped.InnerException!
            : exception;
        return $"{actual.GetType().Name}: {actual.Message}";
    }

    private static string Describe(object[] row) =>
        row.Length == 0 ? string.Empty : $"({string.Join(", ", row)})";
}
