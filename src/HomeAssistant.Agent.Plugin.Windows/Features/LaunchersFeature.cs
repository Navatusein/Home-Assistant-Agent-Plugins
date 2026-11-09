using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Enums;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Windows.Config;
using HomeAssistant.Agent.Plugin.Windows.Launch;
using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Windows.Features;

// A button per configured application and per folder.
internal sealed class LaunchersFeature : PluginFeature
{
    private readonly List<ButtonEntity> _buttons = [];

    public LaunchersFeature()
        : base("launchers", "Launchers", "Buttons that start applications and open folders.", PluginHostMode.Desktop)
    {
        Controls =
        [
            new ComplexListControlDescriptor("Applications", "Applications", "One button per application.",
                new InputTextControlDescriptor("Name", "Name", "Name shown in Home Assistant."),
                new FilePickerControlDescriptor("Path", "Path", "Path to the executable or shortcut.",
                    filter: "Applications|*.exe;*.lnk|Executables|*.exe|Shortcuts|*.lnk|All files|*.*"),
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
        var windowsConfig = (WindowsPluginConfig) config;

        _buttons.Clear();

        foreach (var application in windowsConfig.Applications)
        {
            var slug = Slug.ToObjectId(application.Name);

            _buttons.Add(new ButtonEntity
            {
                Name = $"Launch {application.Name}",
                UniqueId = $"windows_launchers_app_{slug}",
                BaseTopic = $"windows/launchers/app-{slug}",
                Icon = "mdi:application",
                ClickHandler = () =>
                {
                    ProcessLauncher.LaunchApplication(application.Path, application.Arguments);
                    return Task.CompletedTask;
                }
            });
        }

        foreach (var folder in windowsConfig.Folders)
        {
            var slug = Slug.ToObjectId(folder.Name);

            _buttons.Add(new ButtonEntity
            {
                Name = $"Open {folder.Name}",
                UniqueId = $"windows_launchers_folder_{slug}",
                BaseTopic = $"windows/launchers/folder-{slug}",
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
