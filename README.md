# Home Assistant Agent Plugins

The official plugins of [Home Assistant Agent](https://github.com/Navatusein/Home-Assistant-Agent):
Windows, macOS, Discord, Microsoft Teams, OBS Studio and Logitech G HUB.

## Building

The plugins compile against the agent's plugin API. Check out the agent next to
this repository:

```
Projects/
  Home-Assistant-Agent/
  Home-Assistant-Agent-Plugins/
```

Then `dotnet build` builds against the agent's sources and copies each plugin
into that agent's `Plugins` folder, so a change to the API and to a plugin can
be made and debugged together.

Without the agent next to it, the build falls back to the
`HomeAssistant.Agent.Plugin.Sdk` NuGet package (`-p:AgentApi=package` forces
it, `-p:AgentRepoPath=<path>` points to the agent elsewhere). The tests always
need the agent's sources.

## Releasing

Push a tag `plugin/<name>/v<version>`, for example `plugin/discord/v1.2.0`. The
plugin is released and listed in this repository's catalog:

```
https://github.com/Navatusein/Home-Assistant-Agent-Plugins/releases/download/index/index.json
```
