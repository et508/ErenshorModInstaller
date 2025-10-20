using System.Windows;

namespace ErenshorModInstaller.Wpf.UI
{
    public static class Prompts
    {
        public static PromptResult ShowDowngrade(Window owner, string displayName, string installed, string incoming)
        {
            var dlg = new PromptDialog()
                .WithOwner(owner)
                .WithTitle("Lower Version Detected")
                .WithMessage($"A newer version of {displayName} is already installed.")
                .WithDetail($"Installed: {installed}\nIncoming:  {incoming}")
                .WithPrimary("Overwrite")          // overwrite/downgrade
                .WithSecondary("Keep both")        // stash incoming
                .WithCancel("Cancel");

            dlg.ShowDialog();
            return dlg.Result;
        }
        
        public static PromptResult ShowConfirmUninstallSingle(Window owner, string displayName)
        {
            var dlg = new PromptDialog()
                .WithOwner(owner)
                .WithTitle("Uninstall Mod")
                .WithMessage($"Do you want to uninstall {displayName}?")
                .WithPrimary("Uninstall")
                .WithCancel("Cancel");

            dlg.ShowDialog();
            return dlg.Result;
        }
        
        public static PromptResult ShowConfirmUninstallMulti(Window owner, string displayName, string version, IEnumerable<string> versions)
        {
            var storedList = string.Join(", ", (versions ?? Enumerable.Empty<string>())
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
            
            var dlg = new PromptDialog()
                .WithOwner(owner)
                .WithTitle("Uninstall Mod")
                .WithMessage($"Multiple versions of {displayName} exist.?")
                .WithDetail($"Installed: {version}\nStored: {storedList} ")
                .WithPrimary("Uninstall All")
                .WithSecondary("Pick Versions")
                .WithCancel("Cancel");

            dlg.ShowDialog();
            return dlg.Result;
        }
        
        public static PromptResult ShowBepInExInstall()
        {
            var dlg = new PromptDialog()
                .CenteredOnScreen()
                .WithTitle("BepInEx Not Installed")
                .WithMessage($"BepInEx is not installed.\n\nWould you like to download and install BepInEx now?")
                .WithPrimary("Yes")
                .WithCancel("No");

            dlg.ShowDialog();
            return dlg.Result;
        }
        
        public static PromptResult ShowBepInExSetup()
        {
            var dlg = new PromptDialog()
                .CenteredOnScreen()
                .WithTitle("BepInEx Setup")
                .WithMessage($"Erenshor must be launched once so BepInEx can complete its setup.\n\nWould you like to do this now?")
                .WithPrimary("Launch Erenshor")
                .WithCancel("Cancel");

            dlg.ShowDialog();
            return dlg.Result;
        }
        
        public static PromptResult ShowBepInExConfigFix()
        {
            var dlg = new PromptDialog()
                .CenteredOnScreen()
                .WithTitle("BepInEx Setup")
                .WithMessage($"Setting 'HideManagerGameObject' should be TRUE for Erenshor mods to work correctly.\n\nWould you like to do this now?")
                .WithPrimary("Set TRUE")
                .WithCancel("Cancel");

            dlg.ShowDialog();
            return dlg.Result;
        }
        
        public static PromptResult ShowErenshorForceClose()
        {
            var dlg = new PromptDialog()
                .CenteredOnScreen()
                .WithTitle("Close Erenshor")
                .WithMessage($"Erenshor is still running.\nWould you like to force close it now?")
                .WithPrimary("Yes")
                .WithCancel("No");

            dlg.ShowDialog();
            return dlg.Result;
        }
        
        public static PromptResult ShowBepInExSuccess()
        {
            var dlg = new PromptDialog()
                .CenteredOnScreen()
                .WithTitle("BepInEx Success")
                .WithMessage($"\n\nBepInEx has been successfully installed and setup.")
                .WithCancel("OK");

            dlg.ShowDialog();
            return dlg.Result;
        }
        
        public static PromptResult ShowUpdateAvailable(string currentVersion, string newVersion)
        {
            var dlg = new PromptDialog()
                .CenteredOnScreen()
                .WithTitle("New Version")
                .WithMessage($"\n\nA new version is available.\n\nGo to GitHub release page to download. ")
                .WithPrimary("Go To Release")
                .WithCancel("Cancel");

            dlg.ShowDialog();
            return dlg.Result;
        }
    }
}