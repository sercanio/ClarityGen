namespace Myrtus.Clarity.Generator.Common.Models
{
    public class AppSettings
    {
        public TemplateSettings Template { get; set; }
        public List<ModuleSettings> Modules { get; set; } = new List<ModuleSettings>();
    }

    public class ModuleSettings
    {
        public string Name { get; set; }
        public string GitRepoUrl { get; set; }
    }
}
