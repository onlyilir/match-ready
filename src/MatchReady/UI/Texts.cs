namespace MatchReady.UI;

public static class Texts
{
    public const string WhatIWillDo =
        "When you press BOOST, I will:\r\n\r\n" +
        "•  Close background apps that use RAM, CPU or your graphics card: Ollama, LM Studio, Wallpaper Engine, " +
        "OneDrive, game launchers (not Steam), lighting software you don't need, TeamViewer and similar.\r\n" +
        "•  Ask first before closing browsers or OBS, because you could lose unsaved work or a recording.\r\n" +
        "•  Pause Windows Update, search indexing, background downloads and telemetry (they are only paused, " +
        "never turned off).\r\n" +
        "•  Switch to the High performance power plan.\r\n" +
        "•  If you turned it on: pause Windows notifications so popups can't cover the game.\r\n" +
        "•  Make sure Windows Game Mode is on.\r\n\r\n" +
        "When you press RESTORE (or CS2 closes, or you close me), Windows services and settings go back exactly " +
        "as they were. I never open anything: the closed apps stay closed until you open them yourself or " +
        "restart your PC.\r\n\r\n" +
        "I will NEVER touch CS2, Steam, FACEIT, Discord, Norton, your mouse/keyboard/headset software, " +
        "your graphics or audio drivers, or Windows security. I don't change any game files or settings.";

    public const string Tips =
        "Tips you can do yourself (I won't do these for you):\r\n\r\n" +
        "•  Higher CPU priority for CS2: in Steam, right-click Counter-Strike 2 > Properties > Launch options, and add  -high\r\n" +
        "•  FACEIT's own fix for AMD CPUs running slow with the anti-cheat (HPET): see\r\n" +
        "    support.faceit.com/hc/en-us/articles/14605964372124\r\n" +
        "•  Keep the AMD graphics driver up to date in AMD Software.\r\n" +
        "•  If anything looks wrong: press RESTORE EVERYTHING, or just reboot. Nothing I do is permanent.";

    public const string Insider =
        "You're on a Windows Insider (preview) build. FACEIT says its anti-cheat is not supported on Insider " +
        "builds and can crash or blue-screen there. If FACEIT AC ever refuses to start, that's the likely cause. " +
        "A stable Windows 11 is safer.";
}
