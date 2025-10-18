## [1.1.0] - TBD
### Version Detection
- Now detects what version of the ```.dll``` is installed.
  - Will create a small ModIndex file under ```BepInEx/ErenshorModInstaller/``` to track a mods GUID, Name, and Version. 

### BepInEx Install
- Upon first run, if BepInEx is not installed, will prompt if you would like Mod Installer to download and install BepInEx for you.
  - Mod Installer will download and install BepInEx For Erenshor and run a compelete setup, validating everything is correct and ready to use.
- If BepInEx is already installed, it will validate that setup has been compeleted and everything is correct. If not, it will ask if you would like Mod Installer to fix it.

## [1.0.1] - 10/15/2025
### Installed Mods List
- Will now show .dll files directly installed in the plugins folder
- Enable/Disable mods with easy to use checkboxes

### Install Methods
- Supports dropping the .dll directly into the window to install
- Will now keep packaged structure intact with how the author intended the mod to be installed

## [1.0.0] - 10/14/2025
### Release
- Initial release
