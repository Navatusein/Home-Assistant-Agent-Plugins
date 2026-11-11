using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

// The agent reads this attribute (PluginPlatforms) to skip the plugin on
// Windows and to list it in the catalog for macOS only.
[assembly: SupportedOSPlatform("macos")]

[assembly: InternalsVisibleTo("HomeAssistant.Agent.Plugins.Tests")]
