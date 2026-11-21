using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

// The agent reads these attributes (PluginPlatforms) to skip the plugin on
// other systems and to list it in the catalog for Windows and macOS: where
// the desktop app shows notifications.
[assembly: SupportedOSPlatform("windows")]
[assembly: SupportedOSPlatform("macos")]

[assembly: InternalsVisibleTo("HomeAssistant.Agent.Plugins.Tests")]
