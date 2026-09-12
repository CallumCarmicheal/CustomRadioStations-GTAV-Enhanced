using System;
using System.IO;

public class PathHelper
{
    // The original code reflected the private System.IO.Path.MaxPath field.
    // That is a runtime implementation detail and can disappear/change. Keep the
    // original compatibility limit without private reflection. Windows long-path
    // support may allow more, but irrKlang/older plugins are not guaranteed to.
    private const int LegacyMaxPathWithoutNull = 259;

    public static bool IsPathWithinLimits(string fullPathAndFilename)
    {
        return !string.IsNullOrEmpty(fullPathAndFilename) &&
               fullPathAndFilename.Length <= LegacyMaxPathWithoutNull;
    }
}
