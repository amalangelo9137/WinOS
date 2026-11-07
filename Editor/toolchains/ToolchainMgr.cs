using System.Diagnostics;
using System.IO;

namespace WinOS_Editor.toolchains
{
    public class ToolchainManager
    {
        public string? LLVMPath { get; private set; }

        public bool IsAvailable =>
            !string.IsNullOrEmpty(LLVMPath) &&
            File.Exists(Path.Combine(LLVMPath, "clang++.exe"));

        public bool DetectLLVM()
        {
            // Check the default installation directory.
            string defaultPath = @"C:\Program Files\LLVM\bin";

            if (File.Exists(Path.Combine(defaultPath, "clang++.exe")))
            {
                LLVMPath = defaultPath;
                return true;
            }

            // Check directories listed in the system PATH.
            string? pathVariable = Environment.GetEnvironmentVariable("PATH");

            if (!string.IsNullOrEmpty(pathVariable))
            {
                foreach (string directory in pathVariable.Split(';'))
                {
                    string candidate = directory.Trim();

                    if (File.Exists(Path.Combine(candidate, "clang++.exe")))
                    {
                        LLVMPath = candidate;
                        return true;
                    }
                }
            }

            LLVMPath = null;
            return false;
        }

        public async Task<(int ExitCode, string Output, string Errors)> RunClangAsync(
    string sourceFile,
    string arguments = "-fsyntax-only")
        {
            if (!IsAvailable)
                throw new InvalidOperationException("LLVM is not available.");

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(LLVMPath!, "clang++.exe"),
                WorkingDirectory = Path.GetDirectoryName(sourceFile)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add(sourceFile);

            foreach (string argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo };

            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return (
                process.ExitCode,
                await outputTask,
                await errorTask
            );
        }
    }
}