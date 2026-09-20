using System;
using System.Windows;

namespace WinOS_Editor
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (HandleHeadlessCommandLine(e.Args))
            {
                Shutdown();
                return;
            }

            var window = CreateStartupWindow(e.Args);
            MainWindow = window;
            window.Show();
        }

        private static bool HandleHeadlessCommandLine(string[] args)
        {
            if (args.Length == 0)
            {
                return false;
            }

            if (!string.Equals(args[0], "--export-default", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var solutionRoot = ProjectPaths.FindSolutionRoot(AppContext.BaseDirectory);
            if (solutionRoot == null)
            {
                throw new InvalidOperationException("Could not locate the WinOS solution root for export.");
            }

            var document = AppDocument.CreateDefault(solutionRoot);
            string? aliasOverride = null;
            foreach (var arg in args)
            {
                if (arg.StartsWith("--alias=", StringComparison.OrdinalIgnoreCase))
                {
                    aliasOverride = arg.Substring("--alias=".Length);
                }
            }

            if (!string.IsNullOrWhiteSpace(aliasOverride))
            {
                document.PackageAlias = aliasOverride;
            }

            WdexeExporter.ExportDocument(document, solutionRoot, document.PackageAlias);
            return true;
        }

        private static MainWindow CreateStartupWindow(string[] args)
        {
            if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.OrdinalIgnoreCase))
            {
                var candidatePath = args[0].Trim('"');
                if (candidatePath.EndsWith(".wdexe", StringComparison.OrdinalIgnoreCase) &&
                    System.IO.File.Exists(candidatePath))
                {
                    var solutionRoot = ProjectPaths.FindSolutionRoot(AppContext.BaseDirectory);
                    var document = WdexeImporter.ImportDocument(candidatePath, solutionRoot);
                    return new MainWindow(document, candidatePath);
                }
            }

            return new MainWindow();
        }
    }
}
