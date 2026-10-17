namespace Multi;

#if NET10_0
/// <summary>A type present in the net10.0 build only.</summary>
public sealed class Net10Contract;
#else
/// <summary>A type present in the netstandard2.1 build only.</summary>
public sealed class StandardContract;
#endif
