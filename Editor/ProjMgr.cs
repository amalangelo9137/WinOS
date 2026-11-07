using System.IO;
using System.Text.Json;
using System.Windows;

namespace WinOS_Editor
{
    public static class ProjectManager
    {

        /// <summary>
        /// Saves project to path
        /// </summary>
        /// <param name="project">current project</param>
        /// <param name="path">path</param>
        /// <returns>true if successful, false if failed</returns>
        public static bool Save(FrameworkProject project, string path)
        {
            try
            {
                string json = JsonSerializer.Serialize(project);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message,
                    "Exception Caught!",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Loads a project from path
        /// </summary>
        /// <param name="path">path</param>
        /// <returns>project</returns>
        public static FrameworkProject? Load(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<FrameworkProject>(json);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message,
                    "Exception Caught!",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return null;
            }
        }
    }
}