using System.Xml.Linq;

public class RalenProject
{
    public string ProjectFilePath { get; private set; } = "";
    public XDocument Xml { get; private set; } = new XDocument();
    public static RalenProject Load(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            return new RalenProject { ProjectFilePath = path, Xml = doc };
        }
        catch
        {
            return null;
        }
    }

    public string GetLanguage()
    {
        var el = Xml.Descendants("RalenLanguage").FirstOrDefault();
        return el?.Value;
    }
    public string GetVersion()
    {
        var el = Xml.Descendants("RalenLanguageVersion").FirstOrDefault();
        return el?.Value;
    }
}
