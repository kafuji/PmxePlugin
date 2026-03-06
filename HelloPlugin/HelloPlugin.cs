using PEPlugin;
using System;
using System.Windows.Forms;

namespace HelloPlugin;

public sealed class Plugin : IPEPlugin
{
    public string Name => "Hello Plugin";
    public string Version => "0.1.0";
    public string Description => "Displays a simple hello message.";

    public IPEPluginOption Option { get; } =
        new PEPluginOption(false, true, "Hello Plugin");

    public void Run(IPERunArgs args)
    {
        MessageBox.Show(
            "Hello from PmxePlugin!",
            "Hello Plugin",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    public void Dispose()
    {
        // No unmanaged resources to clean up.
    }
}
