# Teamspeak3Plugin
Teamspeak 3 Client Integration Plugin for Macro Deck 3

## Current Features
### Actions
- Un-/Mute Input
- Un-/Mute Output
- Switch to Configured Channel
- Change Nickname to Configured
### Variables
- Input State (un-/muted)
- Output State (un-/muted)
- Current Channelname

Variables update as soon as TeamSpeak reports a change through ClientQuery notifications. A poll every
30 seconds is only the fallback. The plugin talks to the local TeamSpeak client only (`127.0.0.1:25639`)
and keeps two ClientQuery connections open: one for commands, one for notifications.

### Widget
- see current channel with connected users live ( thanks to @manuelmayer-dev )

## Planned Features
### Actions
- Away Options
### Variables
- Talkstate

## Requirements
- Macro Deck >= v3.0.0
- .NET 10.0 Runtime (included with Macro Deck)
- Teamspeak 3 + Client Query Plugin

# TeamSpeak 3 Integration: How to get the ClientQuery API Key  
1. Open your TeamSpeak3 Client
2. Click on `Tools` > `Options` (Default shortcut: ALT+P)![Tools-Options](https://i.imgur.com/ZPOVVP3.png)
3. Click on `Settings`![Addons-ClientQuery-Settings](https://i.imgur.com/cq9s798.png)
4. The API Key should now be shown, copy it
![Addons-ClientQuery-Settings-API-Key](https://i.imgur.com/1XBTvcb.png)
5. Paste the API Key in the settings of the Plugin


# Version History
## 0.6.0
- Event tracked Variables
- reconnecting API connection
- live channel widget 
## 0.5.0
- Migration from MacroDeck v2 Plugin
