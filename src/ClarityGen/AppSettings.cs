namespace Myrtus.Clarity.Generator
{

    public class AppSettings
    {
        public TemplateSettings Template { get; set; }
    }

    public class TemplateSettings
    {
        public string GitRepoUrl { get; set; }
        public string TemplateName { get; set; }
    }
}
