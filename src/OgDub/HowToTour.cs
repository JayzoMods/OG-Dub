namespace OgDub;

public sealed class HowToStep
{
    public required string TargetName { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
}

public static class HowToTour
{
    public static IReadOnlyList<HowToStep> Steps { get; } =
    [
        new()
        {
            TargetName = "StationCombo",
            Title = "Aim a station",
            Body = "Scan or pick the app that is making sound. Whole mix records everything except this boombox when process loopback works."
        },
        new()
        {
            TargetName = "KeepButton",
            Title = "KEEP the last 15 seconds",
            Body = "The KEEP timer fills on the LCD. Punch KEEP to drop that riff in the crate without rolling a full tape."
        },
        new()
        {
            TargetName = "RecButton",
            Title = "Rec a C-60 side",
            Body = "Punch REC. The LCD counts 3-2-1, then the tape rolls for 30 minutes. Stop cancels the countdown. Transport keys are silent so they never hit the WAV."
        },
        new()
        {
            TargetName = "PerSongCheck",
            Title = "Per song and mic",
            Body = "Leave Per song ticked to eject a fresh cassette when Windows reports a new track. Tick Mic (Side B) to record the default microphone onto a second track, padded −12 dB — not mixed into the app tape."
        },
        new()
        {
            TargetName = "CrateList",
            Title = "Crate, star, and trash",
            Body = "Tapes land here two per row. Star copies a take into Favorites. Trash deletes it (you will be asked). Drag a cassette onto Explorer to copy the WAV. Double-click plays Side A."
        },
        new()
        {
            TargetName = "PlayButton",
            Title = "Play, Flip, Cue, Dub",
            Body = "Play Side A. Flip plays Side B when the tile shows a B. Cue jumps marks on Side A. Dub glues the two newest Side A tapes with 2 second gaps."
        },
        new()
        {
            TargetName = "CrateLabel",
            Title = "Open the crate folder",
            Body = "Click CRATE to open the library in File Explorer. Click FAV for Favorites. Folder lets you pick a different crate location. Only record audio you are allowed to record."
        }
    ];
}
