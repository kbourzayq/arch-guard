namespace App;

#if RELEASE_ONLY
/// <summary>A type included in the Release compilation only.</summary>
public sealed class ReleaseContract : Library.IContract;
#else
/// <summary>A type included in the Debug compilation only.</summary>
public sealed class DebugContract : Library.IContract;
#endif
