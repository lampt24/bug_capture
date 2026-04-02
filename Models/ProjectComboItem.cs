using Redmine.Net.Api.Types;

namespace BugCapture.Models
{
    public class ProjectComboItem
    {
        public Project Project { get; set; } = new Project();

        public string DisplayName { get; set; } = string.Empty;

        public override string ToString()
        {
            return DisplayName;
        }
    }
}