# OverlayLock 1.1

A password-protected screen overlay for Windows 10/11. It hides the screen and blocks the keyboard and mouse **without locking Windows**, so these keep running:
- long jobs and scripts
- UI automation and RPA tools
- browser sessions
- remote desktop and VPN connections

If you want, it can also keep the PC from going to sleep while the screen is locked.

## Install
1. Right-click the zip, choose **Properties**, tick **Unblock**, then OK. Only do this after you've looked at the files.
2. Unzip to a folder only you can write to, for example `C:\Users\<you>\Tools\OverlayLock`.
3. Double-click **build.bat**. It compiles `OverlayLock.cs` with the C# compiler built into Windows, adds a **Lock Screen** shortcut to your Desktop, and starts the app.
4. Set a password when the Settings window opens. It must be at least 8 characters.

## Lock / unlock
To lock, do any of these:
- Left-click the shield icon in the system tray
- Press **Ctrl+Alt+L**
- Double-click the **Lock Screen** shortcut
- Leave the PC idle for the number of minutes you set

To unlock, type your password and press **Enter**.

After 3 wrong passwords you have to wait 5 seconds before trying again. Each further miss doubles the wait, up to 5 minutes.

Opening **Settings** or choosing **Exit** from the tray also asks for the password.

## While locked
- Every monitor is covered. If a monitor is plugged in or the resolution changes, the new screen is covered within half a second.
- These keys are blocked: Win, Alt+Tab, Alt+Esc, Alt+F4, Ctrl+Esc and Ctrl+Shift+Esc.
- The shortcuts for Sticky Keys, Filter Keys, Toggle Keys, High Contrast and Mouse Keys are turned off.
- Task Manager is disabled (you can turn this off in Settings).
- The PC is kept awake. You can choose to keep the display on too.

When you unlock, everything goes back to how it was. If the app crashes, or you sign out or lose power while locked, your original settings are restored the next time the app starts.

## Limits
- **Ctrl+Alt+Del still opens the Windows security screen.** From there someone can choose Lock, Switch user or Sign out. Signing out ends your session. They can also restart or turn off the PC with the power button. No app can block these. Anyone with physical access can always pull the power.
- If your company's IT policy already controls Task Manager, OverlayLock leaves your original setting in place when it unlocks. Some managed PCs don't allow apps to change it at all.
- Settings are stored in `%APPDATA%\OverlayLock`. Your password is saved only as a salted hash, never as plain text. **Forgot the password while locked?** Press Ctrl+Alt+Del and choose Sign out. This ends your running jobs. Sign back in. Open a Command Prompt and run `taskkill /im OverlayLock.exe`. Then delete `%APPDATA%\OverlayLock\settings.cfg` and start OverlayLock again.
