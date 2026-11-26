namespace MatchReady.UI;

/// <summary>
/// Longer texts in one place (English only for now; kept short and plain so they can be translated later).
/// Voice: the app is "Match Ready", the user is "you".
/// </summary>
public static class Texts
{
    public const string WhatIWillDo =
        "When you press Boost (or CS2 starts with Auto mode on), Match Ready:\n\n" +
        "•  Closes background apps that use RAM, CPU or graphics memory, such as local AI models (Ollama, LM Studio), " +
        "Wallpaper Engine, OneDrive and game launchers (never Steam).\n" +
        "•  Asks first before closing browsers or OBS, because you could lose unsaved work or a recording.\n" +
        "•  Pauses Windows Update, search indexing, background downloads and telemetry. They're only paused, never turned off.\n" +
        "•  Switches to the High performance power plan and makes sure Windows Game Mode is on.\n" +
        "•  Pauses Windows notifications, only if you turn that on.\n\n" +
        "When you press Restore (or CS2 closes, or you exit), Windows services and settings go back exactly as they were. " +
        "Closed apps stay closed until you open them or restart your PC.\n\n" +
        "It never touches CS2, Steam, FACEIT, Discord, your antivirus, your graphics or audio drivers, Windows security, " +
        "or the mouse, keyboard, headset and fan software you chose to keep. It doesn't change any game files or settings.";

    public const string Tips =
        "•  Higher CPU priority for CS2: in Steam, right-click Counter-Strike 2 > Properties > Launch options, and add  -high\n" +
        "•  AMD CPU running slowly with FACEIT's anti-cheat? FACEIT has its own fix (HPET): see the Pro tab.\n" +
        "•  Keep your graphics driver up to date with the AMD, NVIDIA or Intel app.\n" +
        "•  If anything looks wrong: press Restore everything, or just restart your PC. Nothing Match Ready does is permanent.";

    public const string Insider =
        "You're on a Windows Insider (preview) build. FACEIT says its anti-cheat isn't supported on Insider builds " +
        "and can crash or blue-screen there. If FACEIT's anti-cheat ever refuses to start, that's the likely cause. " +
        "A stable Windows 11 release is safer.";
}
