using System.Reflection;

if (args is [] or ["--help"] or ["-h"])
{
    Console.WriteLine("""
        Architecture Guardian for .NET

        Usage: arch-guard [--help | --version]

          -h, --help   Show usage
          --version    Show version

        The solution scaffold is ready. Analysis commands are not available yet.
        """);
    return 0;
}

if (args is ["--version"])
{
    Console.WriteLine($"Architecture Guardian {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");
    return 0;
}

// Unsupported analysis commands must fail explicitly until a real analysis use case is available.
Console.Error.WriteLine($"Unknown arguments: {string.Join(' ', args)}");
Console.Error.WriteLine("Use --help for usage.");
return 2;
