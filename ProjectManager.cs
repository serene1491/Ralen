using System.Xml.Linq;
/* ---------- Project Manager (.ralenproj) ---------- */
public class ProjectManager
{
    readonly Config cfg;
    public ProjectManager(Config c) { cfg = c; }

    public void CreateConsoleProject(string cwd, string name, string lang, string version)
    {
        var dir = Path.Combine(cwd, name);
        Directory.CreateDirectory(dir);

        var proj = new XElement("Project",
            new XAttribute("Sdk", "Ralen.Project/1.0"),
            new XElement("PropertyGroup",
                new XElement("OutputType", "Exe"),
                new XElement("TargetFramework", lang),
                new XElement("RalenLanguage", lang),
                new XElement("RalenLanguageVersion", version)
            )
        );

        var projPath = Path.Combine(dir, $"{name}.ralenproj");
        proj.Save(projPath);

        var srcDir = Path.Combine(dir, "src");
        Directory.CreateDirectory(srcDir);
        var mainFile = Path.Combine(srcDir, "Program.ralen");
        var content = $"// Project {name}\nstd.print(\"Hello from {name} ({lang} {version})!\")\n";
        File.WriteAllText(mainFile, content);

        Console.WriteLine($"Project created: {projPath}");
    }

    public RalenProject FindProjectInDirectory(string dir)
    {
        string actual = dir;
        if (File.Exists(dir) && Path.GetExtension(dir).Equals(".ralenproj", StringComparison.OrdinalIgnoreCase))
        {
            return RalenProject.Load(Path.GetFullPath(dir));
        }
        if (!Directory.Exists(dir)) return null;
        var projFiles = Directory.GetFiles(dir, "*.ralenproj", SearchOption.TopDirectoryOnly);
        if (projFiles.Length == 0) return null;
        return RalenProject.Load(projFiles[0]);
    }
}
