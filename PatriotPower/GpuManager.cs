using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PatriotPower
{
    public static class GpuManager
    {
        // PowerShell command to find dedicated GPUs (NVIDIA or AMD) and exclude basic/integrated adapters if possible.
        // VID 10DE = NVIDIA
        // VID 1002 = AMD
        private const string GpuQuery = "Get-PnpDevice -Class Display | Where-Object { ($_.InstanceId -match 'VEN_10DE' -or $_.InstanceId -match 'VEN_1002') -and ($_.FriendlyName -notmatch 'Intel') -and ($_.FriendlyName -notmatch 'Basic') }";

        /// <summary>
        /// Checks if the system has more than one active display adapter.
        /// </summary>
        public static async Task<bool> IsSystemSafeForEnduranceMode()
        {
            // Count ALL display adapters (including disabled ones) to verify it's a hybrid system.
            string countCommand = "@(Get-PnpDevice -Class Display).Count";

            string result = await RunPowerShellCommandWithOutput(countCommand);

            if (int.TryParse(result.Trim(), out int count))
            {
                // Safe if we have 2 or more adapters (e.g., Integrated + Dedicated)
                return count > 1;
            }

            return false;
        }

        /// <summary>
        /// Checks if the dGPU is currently disabled.
        /// </summary>
        public static async Task<bool> IsEnduranceModeActiveAsync()
        {
            // If the dGPU's status is not 'OK' (e.g., Error/Disabled), Endurance Mode is active.
            string command = $"@({GpuQuery} | Where-Object {{ $_.Status -ne 'OK' }}).Count";

            string result = await RunPowerShellCommandWithOutput(command);

            if (int.TryParse(result.Trim(), out int count))
            {
                return count > 0;
            }

            return false;
        }

        public static async Task EnableEnduranceModeAsync()
        {
            // Disable dedicated GPU
            string command = $"{GpuQuery} | Disable-PnpDevice -Confirm:$false";
            await RunPowerShellCommand(command);
        }

        public static async Task DisableEnduranceModeAsync()
        {
            // Enable dedicated GPU
            string command = $"{GpuQuery} | Enable-PnpDevice -Confirm:$false";
            await RunPowerShellCommand(command);
        }

        /// <summary>
        /// Synchronously re-enables the dGPU. Used exclusively during application shutdown 
        /// to guarantee the system is restored before the process terminates.
        /// </summary>
        public static void RestoreGpuSynchronously()
        {
            try
            {
                string command = $"{GpuQuery} | Enable-PnpDevice -Confirm:$false";
                var psi = CreatePsi(command);
                var process = Process.Start(psi);
                process?.WaitForExit();
            }
            catch
            {
                // Best effort during shutdown. If it fails, the OS is likely already terminating resources.
            }
        }

        private static Task RunPowerShellCommand(string psCommand)
        {
            return Task.Run(() =>
            {
                var psi = CreatePsi(psCommand);
                var process = Process.Start(psi);
                process?.WaitForExit();
            });
        }

        private static Task<string> RunPowerShellCommandWithOutput(string psCommand)
        {
            return Task.Run(() =>
            {
                var psi = CreatePsi(psCommand);
                // Redirect output to read the count
                psi.RedirectStandardOutput = true;

                var process = Process.Start(psi);
                string output = process?.StandardOutput.ReadToEnd() ?? "0";
                process?.WaitForExit();
                return output;
            });
        }

        private static ProcessStartInfo CreatePsi(string psCommand)
        {
            return new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
    }
}