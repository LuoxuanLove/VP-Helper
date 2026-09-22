using System;
using System.IO;

namespace VPHelper.Core.Tests;

/// <summary>Locates test fixture files copied to the output directory.</summary>
internal static class Fixtures
{
    public static string Dir
    {
        get
        {
            // Use only reviewed fixtures; raw authenticated captures stay local.
            var baseDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDir, "fixtures", "public");
            if (Directory.Exists(candidate)) return candidate;
            // Fallback: walk up to repo tests/fixtures.
            var dir = new DirectoryInfo(baseDir);
            while (dir != null)
            {
                var f = Path.Combine(dir.FullName, "tests", "fixtures", "public");
                if (Directory.Exists(f)) return f;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("fixtures directory not found");
        }
    }

    public static string Read(string relative)
    {
        var path = Path.Combine(Dir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            // Try srk/ subdir.
            var alt = Path.Combine(Dir, "srk", Path.GetFileName(relative));
            if (File.Exists(alt)) return File.ReadAllText(alt);
            throw new FileNotFoundException(path);
        }
        return File.ReadAllText(path);
    }

    /// <summary>Read raw srk fixture JSON text by file name (adds .srk.json if omitted).</summary>
    public static string ReadSrk(string relative)
    {
        var name = relative.EndsWith(".srk.json", StringComparison.Ordinal) ? relative : relative + ".srk.json";
        return Read(name);
    }

    public static bool Exists(string relative)
    {
        var path = Path.Combine(Dir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path)) return true;
        var alt = Path.Combine(Dir, "srk", Path.GetFileName(relative));
        return File.Exists(alt);
    }
}
