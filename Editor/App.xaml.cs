using System.Windows;

namespace WinOS_Editor
{
    public partial class App : Application
    {
        public static string FilePath = string.Empty;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args != null && e.Args.Length > 0)
                FilePath = e.Args[0];
        }
    }
}
