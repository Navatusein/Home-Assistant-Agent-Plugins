using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Enums;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.MacOS.Config;
using HomeAssistant.Agent.Plugin.MacOS.Launch;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.MacOS.Features;

// A button per configured application and per folder.
internal sealed class LaunchersFeature : PluginFeature
{
    private readonly List<ButtonEntity> _buttons = [];

    public LaunchersFeature()
        : base("launchers", "Launchers", "Buttons that open applications and folders.", PluginHostMode.Desktop)
    {
        Controls =
        [
            new ComplexListControlDescriptor("Applications", "Applications", "One button per application.",
                new InputTextControlDescriptor("Name", "Name", "Name shown in Home Assistant."),
                new FilePickerControlDescriptor("Path", "Path", "Path to the application or executable.",
                    filter: "Applications|*.app|All files|*"),
                new InputTextControlDescriptor("Arguments", "Arguments", "Command line arguments (optional).")
            ),
            new ComplexListControlDescriptor("Folders", "Folders", "One button per folder.",
                new InputTextControlDescriptor("Name", "Name", "Name shown in Home Assistant."),
                new FilePickerControlDescriptor("Path", "Path", "Path to the folder.",
                    mode: FilePickerModeEnum.Folder)
            ),
        ];
    }

    public override IEntity[] GetEntities() => [.. _buttons];

    protected override void OnConfigured(IPluginConfig config)
    {
        var macConfig = (MacOsPluginConfig) config;

        _buttons.Clear();

        foreach (var application in macConfig.Applications)
        {
            var slug = Slug.ToObjectId(application.Name);

            _buttons.Add(new ButtonEntity
            {
                Name = $"Launch {application.Name}",
                UniqueId = $"macos_launchers_app_{slug}",
                BaseTopic = $"macos/launchers/app-{slug}",
                Icon = "mdi:application",
                ClickHandler = () =>
                {
                    ProcessLauncher.LaunchApplication(application.Path, application.Arguments);
                    return Task.CompletedTask;
                }
            });
        }

        foreach (var folder in macConfig.Folders)
        {
            var slug = Slug.ToObjectId(folder.Name);

            _buttons.Add(new ButtonEntity
            {
                Name = $"Open {folder.Name}",
                UniqueId = $"macos_launchers_folder_{slug}",
                BaseTopic = $"macos/launchers/folder-{slug}",
                Icon = "mdi:folder-open",
                ClickHandler = () =>
                {
                    ProcessLauncher.OpenFolder(folder.Path);
                    return Task.CompletedTask;
                }
            });
        }
    }
}
