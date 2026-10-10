// Where the pre-game lobby currently is - one value for the whole session,
// server-written on LobbyState and read by every client. Values are
// appended only, never reordered: the enum crosses the network as its
// underlying int.
public enum LobbyPhase
{
    MainMenu,  // everyone on the Lobby panel (name / Appearance / Ready)
    Choice,    // the navigator picks Spells or Equipment, or starts
    Spells,    // every admitted client is on the Skills panel
    Equipment, // every admitted client is on the Equipment panel
    Started    // players exist; the lobby UI is closed
}
